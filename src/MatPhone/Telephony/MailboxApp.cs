using System.Collections.Concurrent;
using MatPhone.Business;
using MatPhone.Core.Audio;
using MatPhone.Core.Security;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.Storage;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Telephony;

/// <summary>
/// The applications that answer a call themselves: the mailbox (greeting, recording, listening with the keys of the phone) and the feature codes ("Nicht
/// stören", logging into a group). There is no speech: the caller hears the recorded greeting and tones (one tone: go ahead, two rising: on, one falling: off,
/// three low: wrong). Listening and deleting is easier in the web client; the phone is the way without a screen.
/// </summary>
public sealed class MailboxApp(Func<PbxSnapshot> snapshot, IServiceScopeFactory scopes, TelephonyLogWriter log, TimeProvider clock, Func<CallManager> calls)
{
    private static readonly short[] Beep = Wav.Tone(1000, 350);
    private static readonly short[] Short = Wav.Tone(1000, 120);
    private static readonly short[] Gap = Wav.Silence(120);
    private static readonly short[] On = [.. Wav.Tone(660, 150), .. Wav.Silence(60), .. Wav.Tone(990, 220)];
    private static readonly short[] Off = Wav.Tone(440, 350);
    private static readonly short[] Wrong = [.. Wav.Tone(330, 150), .. Wav.Silence(80), .. Wav.Tone(330, 150), .. Wav.Silence(80), .. Wav.Tone(330, 150)];

    /// <summary>The recordings that are still being made, so a message is not lost when the engine stops in the middle of one.</summary>
    private readonly ConcurrentDictionary<string, bool> _recording = new();

    // ---------------------------------------------------------------- leaving a message

    public async Task RecordAsync(ActiveCall call, Leg caller, long mailboxId)
    {
        if (!snapshot().Mailboxes.TryGetValue(mailboxId, out var box) || !await caller.AnswerAsync().ConfigureAwait(false) || caller.Media is not { } media)
        {
            call.Result ??= CallResult.Missed;
            calls().Hangup(caller);
            return;
        }

        await media.WaitReadyAsync(TimeSpan.FromSeconds(5), call.Ended.Token).ConfigureAwait(false);

        call.AnswerDate ??= clock.GetUtcNow().UtcDateTime;
        _recording[call.Id] = true;
        try
        {
            var greeting = await GreetingAsync(box).ConfigureAwait(false);
            var skip = new CancellationTokenSource();
            void OnKey(char key)
            {
                if (key == '#')
                {
                    skip.Cancel();
                }
            }

            media.DigitPressed += OnKey;
            try
            {
                // "#" skips the greeting (and ends the recording later).
                await PlayAsync(media, greeting, CancellationTokenSource.CreateLinkedTokenSource(call.Ended.Token, skip.Token).Token).ConfigureAwait(false);
            }
            finally
            {
                media.DigitPressed -= OnKey;
            }

            await PlayAsync(media, Beep, call.Ended.Token).ConfigureAwait(false);
            var samples = await RecordUntilAsync(media, call, TimeSpan.FromSeconds(box.MaxMessageSeconds)).ConfigureAwait(false);
            call.Result = CallResult.Voicemail;
            if (samples.Length >= G711.SampleRate)
            {
                await SaveAsync(box, call, samples).ConfigureAwait(false);
            }
        }
        finally
        {
            _recording.TryRemove(call.Id, out _);
            calls().Hangup(caller);
        }
    }

    private async Task<short[]> RecordUntilAsync(MediaPort media, ActiveCall call, TimeSpan max)
    {
        var buffer = new List<short>(G711.SampleRate * 30);
        var gate = new object();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        media.Connect((payload, _, payloadType) =>
        {
            var pcm = new short[payload.Length];
            G711.Decode(payload, pcm, payloadType == 8);
            lock (gate)
            {
                buffer.AddRange(pcm);
            }
        });
        void OnKey(char key)
        {
            if (key == '#')
            {
                done.TrySetResult();
            }
        }

        media.DigitPressed += OnKey;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(call.Ended.Token);
        timeout.CancelAfter(max);
        using (timeout.Token.Register(() => done.TrySetResult()))
        {
            await done.Task.ConfigureAwait(false);
        }

        media.DigitPressed -= OnKey;
        media.Connect(null);
        lock (gate)
        {
            return [.. buffer];
        }
    }

    private async Task SaveAsync(MailboxInfo box, ActiveCall call, short[] samples)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            var files = scope.ServiceProvider.GetRequiredService<FileStoreService>();
            var now = clock.GetUtcNow().UtcDateTime;
            var wav = Wav.Write(samples);
            var stamp = Fmt.Local(now).ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
            var file = await files.SaveAsync(wav, $"Nachricht_{stamp}_{FileStoreService.SafeFileName(call.Caller.Number ?? "unbekannt")}.wav", "audio/wav").ConfigureAwait(false);
            var message = new MailboxMessage
            {
                MailboxId = box.Id,
                CallerNumber = call.Caller.Number,
                CallerName = call.Caller.Name,
                ReceivedDate = now,
                DurationSeconds = Wav.DurationSeconds(samples.Length),
                AudioFile = file,
            };
            db.MailboxMessage.Add(message);
            await db.SaveChangesAsync().ConfigureAwait(false);
            log.Info(LogCategory.Mailbox, $"New message in the mailbox {box.Name} ({message.DurationSeconds} s)", call.Caller.Number);
            foreach (var user in Owners(box))
            {
                TelephonySignals.NotifyUserState(user);
            }

            await scope.ServiceProvider.GetRequiredService<MailService>().SendVoicemailAsync(box.Id, message, wav).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Error(LogCategory.Mailbox, $"A message for the mailbox {box.Name} could not be saved", ex.ToString(), call.Caller.Number);
        }
    }

    private IEnumerable<long> Owners(MailboxInfo box)
    {
        if (box.UserId is { } user)
        {
            yield return user;
        }

        if (box.GroupId is { } group && snapshot().Groups.TryGetValue(group, out var g))
        {
            foreach (var member in g.Members)
            {
                yield return member.UserId;
            }
        }
    }

    private async Task<short[]> GreetingAsync(MailboxInfo box)
    {
        if (box.GreetingFileId is not { } fileId)
        {
            return Wav.Silence(300);
        }

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            var file = await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId).ConfigureAwait(false);
            if (file is null)
            {
                return Wav.Silence(300);
            }

            var bytes = await scope.ServiceProvider.GetRequiredService<FileStoreService>().ReadAllAsync(file).ConfigureAwait(false);
            return Wav.ReadAs8kMono(bytes) ?? Wav.Silence(300);
        }
        catch (Exception ex)
        {
            log.Warning(LogCategory.Mailbox, $"The greeting of the mailbox {box.Name} could not be read", ex.Message);
            return Wav.Silence(300);
        }
    }

    // ---------------------------------------------------------------- listening from a phone

    public async Task ListenAsync(ActiveCall call, Leg caller, long? mailboxId)
    {
        if (mailboxId is not { } id || !snapshot().Mailboxes.TryGetValue(id, out var box) || caller.UserId is not { } userId)
        {
            caller.Reject(404, "Not Found");
            call.Result = CallResult.Failed;
            return;
        }

        if (!await caller.AnswerAsync().ConfigureAwait(false) || caller.Media is not { } media)
        {
            return;
        }

        await media.WaitReadyAsync(TimeSpan.FromSeconds(5), call.Ended.Token).ConfigureAwait(false);

        call.AnswerDate ??= clock.GetUtcNow().UtcDateTime;
        call.Result = CallResult.Answered;
        var keys = new KeyReader(media);
        try
        {
            var ct = call.Ended.Token;
            // The owner's own phone needs no PIN; a PIN is asked when the mailbox has one and the caller is somebody else.
            if (box.UserId != userId && box.PinHash is not null && !await CheckPinAsync(media, keys, box.PinHash, ct).ConfigureAwait(false))
            {
                return;
            }

            var actor = new Actor(userId, snapshot().Users.GetValueOrDefault(userId)?.Name, false);
            var messages = await MessagesAsync(box.Id).ConfigureAwait(false);
            // One short tone per new message (at most nine), then the messages, the new ones first.
            await PlayAsync(media, Repeat(Short, Math.Min(9, messages.Count(m => m.IsNew))), ct).ConfigureAwait(false);
            await Task.Delay(400, ct).ConfigureAwait(false);
            for (var i = 0; i < messages.Count && !ct.IsCancellationRequested; i++)
            {
                var message = messages[i];
                var audio = await AudioAsync(actor, message.Id).ConfigureAwait(false);
                keys.Clear();
                await PlayAsync(media, Beep, ct).ConfigureAwait(false);
                await PlayAsync(media, audio, ct, stopOnKey: keys).ConfigureAwait(false);
                var key = await keys.NextAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                switch (key)
                {
                    case '1':
                        i--;
                        break;
                    case '7':
                        await DeleteAsync(actor, message.Id).ConfigureAwait(false);
                        await PlayAsync(media, Off, ct).ConfigureAwait(false);
                        break;
                    case '#':
                        i = messages.Count;
                        break;
                }
            }

            await PlayAsync(media, [.. Off, .. Gap, .. Off], ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            keys.Dispose();
            calls().Hangup(caller);
        }
    }

    private static async Task<bool> CheckPinAsync(MediaPort media, KeyReader keys, string pinHash, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await PlayAsync(media, [.. Short, .. Gap, .. Short], ct).ConfigureAwait(false);
            var pin = await keys.ReadUntilHashAsync(8, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            if (pin.Length > 0 && PasswordHasher.Verify(pin, pinHash))
            {
                await PlayAsync(media, On, ct).ConfigureAwait(false);
                return true;
            }

            await PlayAsync(media, Wrong, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<List<(long Id, bool IsNew)>> MessagesAsync(long mailboxId)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
        return [.. (await db.MailboxMessage.AsNoTracking().Where(m => m.MailboxId == mailboxId && m.AudioFileId != null)
            .OrderByDescending(m => m.IsNew).ThenByDescending(m => m.ReceivedDate).Select(m => new { m.Id, m.IsNew }).ToListAsync().ConfigureAwait(false))
            .Select(m => (m.Id, m.IsNew))];
    }

    private async Task<short[]> AudioAsync(Actor actor, long messageId)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
        var audio = await Business.Mailboxes.AudioAsync(db, actor, messageId, scope.ServiceProvider.GetRequiredService<FileStoreService>()).ConfigureAwait(false);
        return audio is { } a ? Wav.ReadAs8kMono(a.Wav) ?? [] : [];
    }

    private async Task DeleteAsync(Actor actor, long messageId)
    {
        using var scope = scopes.CreateScope();
        await Business.Mailboxes.DeleteMessageAsync(scope.ServiceProvider.GetRequiredService<MpDbContext>(), actor, messageId).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- feature codes

    public async Task FeatureAsync(ActiveCall call, Leg caller, RouteFeature feature)
    {
        if (caller.UserId is not { } userId || !await caller.AnswerAsync().ConfigureAwait(false) || caller.Media is not { } media)
        {
            caller.Reject(403, "Forbidden");
            return;
        }

        await media.WaitReadyAsync(TimeSpan.FromSeconds(5), call.Ended.Token).ConfigureAwait(false);

        call.AnswerDate ??= clock.GetUtcNow().UtcDateTime;
        call.Result = CallResult.Answered;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            var actor = new Actor(userId, snapshot().Users.GetValueOrDefault(userId)?.Name, false);
            bool? on = feature.Kind switch
            {
                FeatureKind.ToggleDoNotDisturb => await Business.Users.ToggleDoNotDisturbAsync(db, actor, userId).ConfigureAwait(false),
                FeatureKind.ToggleGroup when feature.GroupId is { } g => await CallGroups.SetLoggedInAsync(db, actor, userId, g, null).ConfigureAwait(false),
                _ => null,
            };
            await Task.Delay(300, call.Ended.Token).ConfigureAwait(false);
            await PlayAsync(media, on switch { true => On, false => Off, _ => Wrong }, call.Ended.Token).ConfigureAwait(false);
            await Task.Delay(200, call.Ended.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            calls().Hangup(caller);
        }
    }

    // ---------------------------------------------------------------- audio

    /// <summary>Sends <paramref name="samples"/> in frames of 20 ms, in real time.</summary>
    private static async Task PlayAsync(MediaPort media, short[] samples, CancellationToken ct, KeyReader? stopOnKey = null)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        for (var offset = 0; offset < samples.Length && !ct.IsCancellationRequested; offset += G711.SamplesPerFrame)
        {
            if (stopOnKey?.HasKey == true)
            {
                return;
            }

            var frame = new short[G711.SamplesPerFrame];
            Array.Copy(samples, offset, frame, 0, Math.Min(G711.SamplesPerFrame, samples.Length - offset));
            media.SendFrame(frame);
            try
            {
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static short[] Repeat(short[] tone, int count)
        => count <= 0 ? [] : [.. Enumerable.Range(0, count).SelectMany(_ => tone.Concat(Gap))];

    /// <summary>The keys a caller presses, one after the other.</summary>
    private sealed class KeyReader : IDisposable
    {
        private readonly MediaPort _media;
        private readonly System.Threading.Channels.Channel<char> _keys = System.Threading.Channels.Channel.CreateUnbounded<char>();

        public KeyReader(MediaPort media)
        {
            _media = media;
            media.DigitPressed += OnKey;
        }

        public bool HasKey => _keys.Reader.Count > 0;

        public void Clear()
        {
            while (_keys.Reader.TryRead(out _))
            {
            }
        }

        public async Task<char?> NextAsync(TimeSpan wait, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(wait);
            try
            {
                return await _keys.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>Digits until "#", a pause of <paramref name="wait"/> or <paramref name="max"/> of them.</summary>
        public async Task<string> ReadUntilHashAsync(int max, TimeSpan wait, CancellationToken ct)
        {
            var digits = new System.Text.StringBuilder();
            while (digits.Length < max)
            {
                var key = await NextAsync(wait, ct).ConfigureAwait(false);
                if (key is null or '#')
                {
                    break;
                }

                if (char.IsAsciiDigit(key.Value))
                {
                    digits.Append(key.Value);
                }
            }

            return digits.ToString();
        }

        public void Dispose() => _media.DigitPressed -= OnKey;

        private void OnKey(char key) => _keys.Writer.TryWrite(key);
    }
}

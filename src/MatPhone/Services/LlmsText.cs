namespace MatPhone.Services;

/// <summary>The text of <c>/llms.txt</c> (llmstxt.org): what this application is, for language models that come across an installation. Says nothing about the installation itself.</summary>
public static class LlmsText
{
    public const string Content = """
        # MatPhone

        > A small telephone system (PBX) with a web client: users, groups with ring strategies, numbers, SIP phones, mailboxes, lines to providers, rules for calls in and out, function keys and call logs. Self-hosted, one container plus PostgreSQL.

        Every page of an installation needs a sign-in; nothing of it is public. The telephony speaks SIP (UDP and TCP, port 5060) to phones and providers and WebRTC to the browser.

        ## Main areas

        - Phone (/Phone): the web client of a signed-in person: dial pad, calls, colleagues with their state, function keys, mailbox.
        - Administration (/Admin): users, teams, groups, numbers, devices, mailboxes, lines and provider profiles, rules, function key templates, logs, settings.
        """;
}

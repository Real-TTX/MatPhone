using System.Linq.Expressions;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Data;

public class MpDbContext(DbContextOptions<MpDbContext> options, ICurrentUser currentUser, TimeProvider clock) : DbContext(options)
{
    public DbSet<User> User => Set<User>();

    public DbSet<UserTotp> UserTotp => Set<UserTotp>();

    public DbSet<UserRecoveryCode> UserRecoveryCode => Set<UserRecoveryCode>();

    public DbSet<UserSession> UserSession => Set<UserSession>();

    public DbSet<Team> Team => Set<Team>();

    public DbSet<TeamMember> TeamMember => Set<TeamMember>();

    public DbSet<CallGroup> CallGroup => Set<CallGroup>();

    public DbSet<CallGroupMember> CallGroupMember => Set<CallGroupMember>();

    public DbSet<PhoneNumber> PhoneNumber => Set<PhoneNumber>();

    public DbSet<Device> Device => Set<Device>();

    public DbSet<Mailbox> Mailbox => Set<Mailbox>();

    public DbSet<MailboxMessage> MailboxMessage => Set<MailboxMessage>();

    public DbSet<LineProfile> LineProfile => Set<LineProfile>();

    public DbSet<Line> Line => Set<Line>();

    public DbSet<Rule> Rule => Set<Rule>();

    public DbSet<KeyTemplate> KeyTemplate => Set<KeyTemplate>();

    public DbSet<FunctionKey> FunctionKey => Set<FunctionKey>();

    public DbSet<CallRecord> CallRecord => Set<CallRecord>();

    public DbSet<CallParticipant> CallParticipant => Set<CallParticipant>();

    public DbSet<TelephonyLog> TelephonyLog => Set<TelephonyLog>();

    public DbSet<StoredFile> StoredFile => Set<StoredFile>();

    public DbSet<ActivityLog> ActivityLog => Set<ActivityLog>();

    public DbSet<BackupTarget> BackupTarget => Set<BackupTarget>();

    public DbSet<BackupPlan> BackupPlan => Set<BackupPlan>();

    public DbSet<BackupRun> BackupRun => Set<BackupRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Tables are PascalCase and named like the entity; enums are stored as text; money has two decimals.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            entityType.SetTableName(entityType.ClrType.Name);

            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                // UpdateState is a number on purpose (0 = deleted, 1 = created, 2 = updated); DayOfWeek is one too.
                if (type.IsEnum && type != typeof(DayOfWeek) && type != typeof(UpdateState))
                {
                    property.SetProviderClrType(typeof(string));
                }
                else if (type == typeof(decimal) && property.GetColumnType() is null)
                {
                    property.SetColumnType("numeric(18,2)");
                }
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                b.Entity(entityType.ClrType).HasQueryFilter(NotDeleted(entityType.ClrType));
            }
        }

        b.Entity<User>().Ignore(x => x.Initials);
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.HasOne(x => x.Totp).WithOne(x => x.User).HasForeignKey<UserTotp>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserTotp>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<UserRecoveryCode>().HasOne(x => x.UserTotp).WithMany(x => x.RecoveryCodes).HasForeignKey(x => x.UserTotpId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<User>(e =>
        {
            e.HasOne<PhoneNumber>().WithMany().HasForeignKey(x => x.OutboundNumberId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<KeyTemplate>().WithMany().HasForeignKey(x => x.KeyTemplateId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Team>().HasIndex(x => x.Name);
        b.Entity<TeamMember>(e =>
        {
            e.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
            e.HasOne(x => x.Team).WithMany(x => x.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CallGroup>().HasIndex(x => x.Name);
        b.Entity<CallGroupMember>(e =>
        {
            e.HasIndex(x => new { x.CallGroupId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.CallGroup).WithMany(x => x.Members).HasForeignKey(x => x.CallGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // A number exists once among the numbers that are not deleted: the engine finds the target of a call by it.
        b.Entity<PhoneNumber>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CallGroup).WithMany().HasForeignKey(x => x.CallGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Mailbox).WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Device>(e =>
        {
            e.HasIndex(x => x.SipUser).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.HasIndex(x => x.MacAddress).HasFilter("\"MacAddress\" IS NOT NULL");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.KeyTemplate).WithMany().HasForeignKey(x => x.KeyTemplateId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Mailbox>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CallGroup).WithMany().HasForeignKey(x => x.CallGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.GreetingFile).WithMany().HasForeignKey(x => x.GreetingFileId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<MailboxMessage>(e =>
        {
            e.HasIndex(x => new { x.MailboxId, x.ReceivedDate });
            e.HasOne(x => x.Mailbox).WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AudioFile).WithMany().HasForeignKey(x => x.AudioFileId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LineProfile>().HasIndex(x => x.Name);
        b.Entity<Line>().HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Rule>(e =>
        {
            e.HasIndex(x => new { x.Direction, x.Position });
            e.HasOne(x => x.PhoneNumber).WithMany().HasForeignKey(x => x.PhoneNumberId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<FunctionKey>(e =>
        {
            e.HasIndex(x => new { x.KeyTemplateId, x.Position });
            e.HasIndex(x => new { x.UserId, x.Position });
            e.HasOne(x => x.KeyTemplate).WithMany(x => x.Keys).HasForeignKey(x => x.KeyTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetGroup).WithMany().HasForeignKey(x => x.TargetGroupId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CallRecord>(e =>
        {
            e.HasIndex(x => x.StartDate);
            e.HasIndex(x => x.CallId);
        });
        b.Entity<CallParticipant>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CallRecordId });
            e.HasOne(x => x.CallRecord).WithMany(x => x.Participants).HasForeignKey(x => x.CallRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TelephonyLog>(e =>
        {
            e.HasIndex(x => x.OccurredDate);
            e.HasIndex(x => new { x.Category, x.OccurredDate });
        });

        b.Entity<StoredFile>().HasIndex(x => x.Token).IsUnique();
        b.Entity<ActivityLog>().HasIndex(x => x.OccurredDate);
        b.Entity<BackupPlan>().HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BackupRun>().HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.SetNull);
    }

    private static LambdaExpression NotDeleted(Type clrType)
    {
        var parameter = Expression.Parameter(clrType, "e");
        var state = Expression.Property(parameter, nameof(ISoftDeletable.UpdateState));
        var body = Expression.NotEqual(state, Expression.Constant(UpdateState.Deleted));
        return Expression.Lambda(body, parameter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills the audit columns and turns a delete of a soft-deletable record into a state change.</summary>
    private void Stamp()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditedEntity>().ToList())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable soft)
            {
                entry.State = EntityState.Modified;
                soft.UpdateState = UpdateState.Deleted;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreateDate == default)
                    {
                        entry.Entity.CreateDate = now;
                    }

                    entry.Entity.CreateUserId ??= userId;
                    entry.Entity.UpdateDate = entry.Entity.CreateDate;
                    entry.Entity.UpdateUserId = entry.Entity.CreateUserId;
                    if (entry.Entity is ISoftDeletable added && added.UpdateState == UpdateState.Deleted)
                    {
                        added.UpdateState = UpdateState.Created;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdateDate = now;
                    entry.Entity.UpdateUserId = userId;
                    entry.Property(nameof(AuditedEntity.CreateDate)).IsModified = false;
                    entry.Property(nameof(AuditedEntity.CreateUserId)).IsModified = false;
                    if (entry.Entity is ISoftDeletable modified && modified.UpdateState == UpdateState.Created)
                    {
                        modified.UpdateState = UpdateState.Updated;
                    }

                    break;
            }
        }
    }
}

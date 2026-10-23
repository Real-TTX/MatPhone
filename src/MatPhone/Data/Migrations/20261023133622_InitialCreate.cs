using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MatPhone.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupTarget",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Host = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Share = table.Column<string>(type: "text", nullable: true),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    PasswordProtected = table.Column<string>(type: "text", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupTarget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallGroup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Strategy = table.Column<string>(type: "text", nullable: false),
                    RingSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxWaitSeconds = table.Column<int>(type: "integer", nullable: false),
                    Fallback = table.Column<string>(type: "text", nullable: false),
                    FallbackUserId = table.Column<long>(type: "bigint", nullable: true),
                    FallbackGroupId = table.Column<long>(type: "bigint", nullable: true),
                    FallbackMailboxId = table.Column<long>(type: "bigint", nullable: true),
                    FallbackNumber = table.Column<string>(type: "text", nullable: true),
                    CallerNamePrefix = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CallId = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnswerDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CallerNumber = table.Column<string>(type: "text", nullable: true),
                    CallerName = table.Column<string>(type: "text", nullable: true),
                    CalledNumber = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    CallGroupId = table.Column<long>(type: "bigint", nullable: true),
                    AnsweredByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeyTemplate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LineProfile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    Host = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Transport = table.Column<string>(type: "text", nullable: false),
                    Register = table.Column<bool>(type: "boolean", nullable: false),
                    RegisterExpirySeconds = table.Column<int>(type: "integer", nullable: false),
                    OutboundProxy = table.Column<string>(type: "text", nullable: true),
                    CallerIdMode = table.Column<string>(type: "text", nullable: false),
                    NumberFormat = table.Column<string>(type: "text", nullable: false),
                    UserNameHint = table.Column<string>(type: "text", nullable: true),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredFile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Team",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelephonyLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    RemoteAddress = table.Column<string>(type: "text", nullable: true),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    DeviceId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelephonyLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupPlan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    TimeOfDay = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    KeepLast = table.Column<int>(type: "integer", nullable: false),
                    KeepDaily = table.Column<int>(type: "integer", nullable: false),
                    KeepWeekly = table.Column<int>(type: "integer", nullable: false),
                    KeepMonthly = table.Column<int>(type: "integer", nullable: false),
                    PassphraseProtected = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "text", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupPlan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupPlan_BackupTarget_TargetId",
                        column: x => x.TargetId,
                        principalTable: "BackupTarget",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Line",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProfileId = table.Column<long>(type: "bigint", nullable: true),
                    Host = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Transport = table.Column<string>(type: "text", nullable: false),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    OutboundProxy = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    AuthUserName = table.Column<string>(type: "text", nullable: true),
                    PasswordProtected = table.Column<string>(type: "text", nullable: false),
                    Register = table.Column<bool>(type: "boolean", nullable: false),
                    RegisterExpirySeconds = table.Column<int>(type: "integer", nullable: false),
                    CallerIdMode = table.Column<string>(type: "text", nullable: false),
                    NumberFormat = table.Column<string>(type: "text", nullable: false),
                    DefaultCallerNumber = table.Column<string>(type: "text", nullable: true),
                    MaxChannels = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastStatus = table.Column<string>(type: "text", nullable: true),
                    LastStatusDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Line_LineProfile_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "LineProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BackupRun",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<long>(type: "bigint", nullable: true),
                    PlanName = table.Column<string>(type: "text", nullable: true),
                    TargetName = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRun", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRun_BackupPlan_PlanId",
                        column: x => x.PlanId,
                        principalTable: "BackupPlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CallGroupMember",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CallGroupId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsLoggedIn = table.Column<bool>(type: "boolean", nullable: false),
                    LastCallDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallGroupMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallGroupMember_CallGroup_CallGroupId",
                        column: x => x.CallGroupId,
                        principalTable: "CallGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CallParticipant",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CallRecordId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IsCaller = table.Column<bool>(type: "boolean", nullable: false),
                    Answered = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallParticipant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallParticipant_CallRecord_CallRecordId",
                        column: x => x.CallRecordId,
                        principalTable: "CallRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Device",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Vendor = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    MacAddress = table.Column<string>(type: "text", nullable: true),
                    SipUser = table.Column<string>(type: "text", nullable: false),
                    SipPasswordProtected = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    KeyTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastRegisterDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUserAgent = table.Column<string>(type: "text", nullable: true),
                    LastAddress = table.Column<string>(type: "text", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Device", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Device_KeyTemplate_KeyTemplateId",
                        column: x => x.KeyTemplateId,
                        principalTable: "KeyTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FunctionKey",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KeyTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<long>(type: "bigint", nullable: true),
                    TargetGroupId = table.Column<long>(type: "bigint", nullable: true),
                    Number = table.Column<string>(type: "text", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FunctionKey_CallGroup_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "CallGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FunctionKey_KeyTemplate_KeyTemplateId",
                        column: x => x.KeyTemplateId,
                        principalTable: "KeyTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Mailbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    CallGroupId = table.Column<long>(type: "bigint", nullable: true),
                    PinHash = table.Column<string>(type: "text", nullable: true),
                    GreetingFileId = table.Column<long>(type: "bigint", nullable: true),
                    MaxMessageSeconds = table.Column<int>(type: "integer", nullable: false),
                    MailNotify = table.Column<bool>(type: "boolean", nullable: false),
                    MailAddress = table.Column<string>(type: "text", nullable: true),
                    MailAttachAudio = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mailbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mailbox_CallGroup_CallGroupId",
                        column: x => x.CallGroupId,
                        principalTable: "CallGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Mailbox_StoredFile_GreetingFileId",
                        column: x => x.GreetingFileId,
                        principalTable: "StoredFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MailboxMessage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailboxId = table.Column<long>(type: "bigint", nullable: false),
                    CallerNumber = table.Column<string>(type: "text", nullable: true),
                    CallerName = table.Column<string>(type: "text", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    AudioFileId = table.Column<long>(type: "bigint", nullable: true),
                    IsNew = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailboxMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailboxMessage_Mailbox_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MailboxMessage_StoredFile_AudioFileId",
                        column: x => x.AudioFileId,
                        principalTable: "StoredFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhoneNumber",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    Target = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    CallGroupId = table.Column<long>(type: "bigint", nullable: true),
                    MailboxId = table.Column<long>(type: "bigint", nullable: true),
                    TargetNumber = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneNumber", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneNumber_CallGroup_CallGroupId",
                        column: x => x.CallGroupId,
                        principalTable: "CallGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhoneNumber_Line_LineId",
                        column: x => x.LineId,
                        principalTable: "Line",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhoneNumber_Mailbox_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Rule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    TimeFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TimeTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Pattern = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberId = table.Column<long>(type: "bigint", nullable: true),
                    CallerUserId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Target = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<long>(type: "bigint", nullable: true),
                    TargetGroupId = table.Column<long>(type: "bigint", nullable: true),
                    TargetMailboxId = table.Column<long>(type: "bigint", nullable: true),
                    TargetNumber = table.Column<string>(type: "text", nullable: true),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    StripDigits = table.Column<int>(type: "integer", nullable: false),
                    Prefix = table.Column<string>(type: "text", nullable: true),
                    CallerNumber = table.Column<string>(type: "text", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rule_Line_LineId",
                        column: x => x.LineId,
                        principalTable: "Line",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Rule_PhoneNumber_PhoneNumberId",
                        column: x => x.PhoneNumberId,
                        principalTable: "PhoneNumber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Theme = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    WebClientEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DoNotDisturb = table.Column<bool>(type: "boolean", nullable: false),
                    ForwardAlways = table.Column<string>(type: "text", nullable: true),
                    ForwardBusy = table.Column<string>(type: "text", nullable: true),
                    ForwardNoAnswer = table.Column<string>(type: "text", nullable: true),
                    NoAnswerSeconds = table.Column<int>(type: "integer", nullable: false),
                    OutboundNumberId = table.Column<long>(type: "bigint", nullable: true),
                    KeyTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    MailboxOnNoAnswer = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLogins = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordChangedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateState = table.Column<short>(type: "smallint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                    table.ForeignKey(
                        name: "FK_User_KeyTemplate_KeyTemplateId",
                        column: x => x.KeyTemplateId,
                        principalTable: "KeyTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_User_PhoneNumber_OutboundNumberId",
                        column: x => x.OutboundNumberId,
                        principalTable: "PhoneNumber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamMember",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMember_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMember_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSession",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    RevokedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSession_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTotp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedStep = table.Column<long>(type: "bigint", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTotp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTotp_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRecoveryCode",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserTotpId = table.Column<long>(type: "bigint", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    UsedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecoveryCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecoveryCode_UserTotp_UserTotpId",
                        column: x => x.UserTotpId,
                        principalTable: "UserTotp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_OccurredDate",
                table: "ActivityLog",
                column: "OccurredDate");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlan_TargetId",
                table: "BackupPlan",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRun_PlanId",
                table: "BackupRun",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CallGroup_Name",
                table: "CallGroup",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CallGroupMember_CallGroupId_UserId",
                table: "CallGroupMember",
                columns: new[] { "CallGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallGroupMember_UserId",
                table: "CallGroupMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallParticipant_CallRecordId",
                table: "CallParticipant",
                column: "CallRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_CallParticipant_UserId_CallRecordId",
                table: "CallParticipant",
                columns: new[] { "UserId", "CallRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_CallRecord_CallId",
                table: "CallRecord",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecord_StartDate",
                table: "CallRecord",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Device_KeyTemplateId",
                table: "Device",
                column: "KeyTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Device_MacAddress",
                table: "Device",
                column: "MacAddress",
                filter: "\"MacAddress\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Device_SipUser",
                table: "Device",
                column: "SipUser",
                unique: true,
                filter: "\"UpdateState\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_Device_UserId",
                table: "Device",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionKey_KeyTemplateId_Position",
                table: "FunctionKey",
                columns: new[] { "KeyTemplateId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionKey_TargetGroupId",
                table: "FunctionKey",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionKey_TargetUserId",
                table: "FunctionKey",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionKey_UserId_Position",
                table: "FunctionKey",
                columns: new[] { "UserId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Line_ProfileId",
                table: "Line",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LineProfile_Name",
                table: "LineProfile",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Mailbox_CallGroupId",
                table: "Mailbox",
                column: "CallGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Mailbox_GreetingFileId",
                table: "Mailbox",
                column: "GreetingFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Mailbox_UserId",
                table: "Mailbox",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MailboxMessage_AudioFileId",
                table: "MailboxMessage",
                column: "AudioFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MailboxMessage_MailboxId_ReceivedDate",
                table: "MailboxMessage",
                columns: new[] { "MailboxId", "ReceivedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumber_CallGroupId",
                table: "PhoneNumber",
                column: "CallGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumber_LineId",
                table: "PhoneNumber",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumber_MailboxId",
                table: "PhoneNumber",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumber_Number",
                table: "PhoneNumber",
                column: "Number",
                unique: true,
                filter: "\"UpdateState\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumber_UserId",
                table: "PhoneNumber",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Rule_Direction_Position",
                table: "Rule",
                columns: new[] { "Direction", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Rule_LineId",
                table: "Rule",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_Rule_PhoneNumberId",
                table: "Rule",
                column: "PhoneNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFile_Token",
                table: "StoredFile",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Team_Name",
                table: "Team",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_TeamId_UserId",
                table: "TeamMember",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMember_UserId",
                table: "TeamMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TelephonyLog_Category_OccurredDate",
                table: "TelephonyLog",
                columns: new[] { "Category", "OccurredDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TelephonyLog_OccurredDate",
                table: "TelephonyLog",
                column: "OccurredDate");

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true,
                filter: "\"UpdateState\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_User_KeyTemplateId",
                table: "User",
                column: "KeyTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_User_OutboundNumberId",
                table: "User",
                column: "OutboundNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecoveryCode_UserTotpId",
                table: "UserRecoveryCode",
                column: "UserTotpId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_Token",
                table: "UserSession",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_UserId",
                table: "UserSession",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTotp_UserId",
                table: "UserTotp",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CallGroupMember_User_UserId",
                table: "CallGroupMember",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CallParticipant_User_UserId",
                table: "CallParticipant",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Device_User_UserId",
                table: "Device",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FunctionKey_User_TargetUserId",
                table: "FunctionKey",
                column: "TargetUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FunctionKey_User_UserId",
                table: "FunctionKey",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Mailbox_User_UserId",
                table: "Mailbox",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PhoneNumber_User_UserId",
                table: "PhoneNumber",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Mailbox_CallGroup_CallGroupId",
                table: "Mailbox");

            migrationBuilder.DropForeignKey(
                name: "FK_PhoneNumber_CallGroup_CallGroupId",
                table: "PhoneNumber");

            migrationBuilder.DropForeignKey(
                name: "FK_Mailbox_User_UserId",
                table: "Mailbox");

            migrationBuilder.DropForeignKey(
                name: "FK_PhoneNumber_User_UserId",
                table: "PhoneNumber");

            migrationBuilder.DropTable(
                name: "ActivityLog");

            migrationBuilder.DropTable(
                name: "BackupRun");

            migrationBuilder.DropTable(
                name: "CallGroupMember");

            migrationBuilder.DropTable(
                name: "CallParticipant");

            migrationBuilder.DropTable(
                name: "Device");

            migrationBuilder.DropTable(
                name: "FunctionKey");

            migrationBuilder.DropTable(
                name: "MailboxMessage");

            migrationBuilder.DropTable(
                name: "Rule");

            migrationBuilder.DropTable(
                name: "TeamMember");

            migrationBuilder.DropTable(
                name: "TelephonyLog");

            migrationBuilder.DropTable(
                name: "UserRecoveryCode");

            migrationBuilder.DropTable(
                name: "UserSession");

            migrationBuilder.DropTable(
                name: "BackupPlan");

            migrationBuilder.DropTable(
                name: "CallRecord");

            migrationBuilder.DropTable(
                name: "Team");

            migrationBuilder.DropTable(
                name: "UserTotp");

            migrationBuilder.DropTable(
                name: "BackupTarget");

            migrationBuilder.DropTable(
                name: "CallGroup");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "KeyTemplate");

            migrationBuilder.DropTable(
                name: "PhoneNumber");

            migrationBuilder.DropTable(
                name: "Line");

            migrationBuilder.DropTable(
                name: "Mailbox");

            migrationBuilder.DropTable(
                name: "LineProfile");

            migrationBuilder.DropTable(
                name: "StoredFile");
        }
    }
}

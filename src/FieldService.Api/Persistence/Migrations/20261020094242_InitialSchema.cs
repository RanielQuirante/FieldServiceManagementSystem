using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814

namespace FieldService.Api.Persistence.Migrations
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fieldservice");

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                    table.CheckConstraint("ck_checklist_templates_trade", "trade IN ('Hvac', 'Electrical', 'Plumbing', 'FireSafety')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_signed_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_display_name_not_blank", "btrim(display_name) <> ''");
                    table.CheckConstraint("ck_users_email_format", "email LIKE '_%@_%'");
                    table.CheckConstraint("ck_users_email_lower_case", "email = lower(email)");
                    table.CheckConstraint("ck_users_role", "role IN ('Administrator', 'Technician')");
                    table.CheckConstraint("ck_users_updated_at", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "checklist_items",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    prompt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_items", x => x.id);
                    table.CheckConstraint("ck_checklist_items_position", "position BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "fk_checklist_items_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "fieldservice",
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "technicians",
                schema: "fieldservice",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_on_shift = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technicians", x => x.user_id);
                    table.CheckConstraint("ck_technicians_phone_number_not_blank", "btrim(phone_number) <> ''");
                    table.CheckConstraint("ck_technicians_trade", "trade IN ('Hvac', 'Electrical', 'Plumbing', 'FireSafety')");
                    table.ForeignKey(
                        name: "fk_technicians_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "fieldservice",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idle_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_client", "client IN ('Web', 'Mobile')");
                    table.CheckConstraint("ck_user_sessions_expiry", "idle_expires_at > created_at AND absolute_expires_at > created_at AND idle_expires_at <= absolute_expires_at");
                    table.CheckConstraint("ck_user_sessions_last_refreshed_at", "last_refreshed_at IS NULL OR last_refreshed_at >= created_at");
                    table.CheckConstraint("ck_user_sessions_revocation", "(revoked_at IS NULL) = (revoked_reason IS NULL)");
                    table.CheckConstraint("ck_user_sessions_revoked_reason", "revoked_reason IN ('SignOut', 'RefreshTokenReuse', 'UserInactive', 'Operator')");
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "fieldservice",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checklist_reading_fields",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_reading_fields", x => x.id);
                    table.UniqueConstraint("ak_checklist_reading_fields_checklist_item_id_id", x => new { x.checklist_item_id, x.id });
                    table.CheckConstraint("ck_checklist_reading_fields_position", "position BETWEEN 1 AND 9");
                    table.ForeignKey(
                        name: "fk_checklist_reading_fields_checklist_items_checklist_item_id",
                        column: x => x.checklist_item_id,
                        principalSchema: "fieldservice",
                        principalTable: "checklist_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1001', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    site_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    site_access_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    equipment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checklist_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_assigned_at", "(assigned_technician_id IS NULL) = (assigned_at IS NULL)");
                    table.CheckConstraint("ck_jobs_assignee", "(status <> 'Unassigned' OR assigned_technician_id IS NULL) AND (status NOT IN ('Assigned', 'InProgress', 'OnHold', 'Completed') OR assigned_technician_id IS NOT NULL)");
                    table.CheckConstraint("ck_jobs_cancelled_at", "(status = 'Cancelled') = (cancelled_at IS NOT NULL)");
                    table.CheckConstraint("ck_jobs_completed_at", "(status = 'Completed') = (completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_jobs_optional_text_not_blank", "(site_access_notes IS NULL OR btrim(site_access_notes) <> '') AND (contact_name IS NULL OR btrim(contact_name) <> '') AND (contact_phone IS NULL OR btrim(contact_phone) <> '') AND (equipment IS NULL OR btrim(equipment) <> '')");
                    table.CheckConstraint("ck_jobs_priority", "priority IN ('Low', 'Normal', 'High', 'Urgent')");
                    table.CheckConstraint("ck_jobs_required_text_not_blank", "btrim(title) <> '' AND btrim(description) <> '' AND btrim(customer_name) <> '' AND btrim(site_address) <> ''");
                    table.CheckConstraint("ck_jobs_schedule", "scheduled_end > scheduled_start");
                    table.CheckConstraint("ck_jobs_started_at", "CASE WHEN status IN ('InProgress', 'OnHold', 'Completed') THEN started_at IS NOT NULL WHEN status IN ('Unassigned', 'Assigned') THEN started_at IS NULL ELSE true END");
                    table.CheckConstraint("ck_jobs_status", "status IN ('Unassigned', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_jobs_timestamps", "updated_at >= created_at AND (completed_at IS NULL OR completed_at >= started_at)");
                    table.ForeignKey(
                        name: "fk_jobs_checklist_templates_checklist_template_id",
                        column: x => x.checklist_template_id,
                        principalSchema: "fieldservice",
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_technicians_assigned_technician_id",
                        column: x => x.assigned_technician_id,
                        principalSchema: "fieldservice",
                        principalTable: "technicians",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "fieldservice",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_refresh_tokens_replacement", "replaced_by_id IS NULL OR (consumed_at IS NOT NULL AND replaced_by_id <> id)");
                    table.CheckConstraint("ck_refresh_tokens_token_hash_length", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalSchema: "fieldservice",
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_user_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "fieldservice",
                        principalTable: "user_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    technician_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => x.id);
                    table.CheckConstraint("ck_inspections_technician_notes", "technician_notes IS NULL OR (submitted_at IS NOT NULL AND btrim(technician_notes) <> '')");
                    table.CheckConstraint("ck_inspections_timestamps", "updated_at >= created_at AND (submitted_at IS NULL OR submitted_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_inspections_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "fieldservice",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspections_technicians_technician_id",
                        column: x => x.technician_id,
                        principalSchema: "fieldservice",
                        principalTable: "technicians",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_history",
                schema: "fieldservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_history", x => x.id);
                    table.CheckConstraint("ck_job_history_action", "action IN ('Created', 'Assigned', 'Started', 'PutOnHold', 'Resumed', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_job_history_from_status", "from_status IN ('Unassigned', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_job_history_from_status_presence", "(action = 'Created') = (from_status IS NULL)");
                    table.CheckConstraint("ck_job_history_note", "CASE WHEN action IN ('PutOnHold', 'Cancelled') THEN note IS NOT NULL AND btrim(note) <> '' ELSE note IS NULL END");
                    table.CheckConstraint("ck_job_history_technician", "(action = 'Assigned') = (technician_id IS NOT NULL)");
                    table.CheckConstraint("ck_job_history_to_status", "to_status = CASE action WHEN 'Created' THEN 'Unassigned' WHEN 'Assigned' THEN 'Assigned' WHEN 'Started' THEN 'InProgress' WHEN 'PutOnHold' THEN 'OnHold' WHEN 'Resumed' THEN 'InProgress' WHEN 'Completed' THEN 'Completed' WHEN 'Cancelled' THEN 'Cancelled' END");
                    table.ForeignKey(
                        name: "fk_job_history_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "fieldservice",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_history_technicians_technician_id",
                        column: x => x.technician_id,
                        principalSchema: "fieldservice",
                        principalTable: "technicians",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_history_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "fieldservice",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inspection_answers",
                schema: "fieldservice",
                columns: table => new
                {
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_answers", x => new { x.inspection_id, x.checklist_item_id });
                    table.CheckConstraint("ck_inspection_answers_note", "(note IS NULL OR btrim(note) <> '') AND (result <> 'Fail' OR note IS NOT NULL)");
                    table.CheckConstraint("ck_inspection_answers_result", "result IN ('Pass', 'Fail', 'NotApplicable')");
                    table.ForeignKey(
                        name: "fk_inspection_answers_checklist_items_checklist_item_id",
                        column: x => x.checklist_item_id,
                        principalSchema: "fieldservice",
                        principalTable: "checklist_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspection_answers_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalSchema: "fieldservice",
                        principalTable: "inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspection_readings",
                schema: "fieldservice",
                columns: table => new
                {
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reading_field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_readings", x => new { x.inspection_id, x.checklist_item_id, x.reading_field_id });
                    table.ForeignKey(
                        name: "fk_inspection_readings_checklist_reading_fields",
                        columns: x => new { x.checklist_item_id, x.reading_field_id },
                        principalSchema: "fieldservice",
                        principalTable: "checklist_reading_fields",
                        principalColumns: new[] { "checklist_item_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspection_readings_inspection_answers",
                        columns: x => new { x.inspection_id, x.checklist_item_id },
                        principalSchema: "fieldservice",
                        principalTable: "inspection_answers",
                        principalColumns: new[] { "inspection_id", "checklist_item_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "fieldservice",
                table: "checklist_templates",
                columns: new[] { "id", "name", "trade" },
                values: new object[,]
                {
                    { new Guid("01a15175-3c00-7000-8000-000101000000"), "General electrical inspection", "Electrical" },
                    { new Guid("01a15175-3c00-7000-8000-000102000000"), "Rooftop HVAC service", "Hvac" },
                    { new Guid("01a15175-3c00-7000-8000-000103000000"), "Boiler service", "Plumbing" },
                    { new Guid("01a15175-3c00-7000-8000-000104000000"), "Fire extinguisher inspection", "FireSafety" },
                    { new Guid("01a15175-3c00-7000-8000-000105000000"), "Electrical panel inspection", "Electrical" }
                });

            migrationBuilder.InsertData(
                schema: "fieldservice",
                table: "checklist_items",
                columns: new[] { "id", "position", "prompt", "template_id" },
                values: new object[,]
                {
                    { new Guid("01a15175-3c00-7000-8000-000201010000"), (short)1, "Measure supply voltage", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201020000"), (short)2, "Isolate and lock out the circuits being worked on", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201030000"), (short)3, "Inspect wiring, terminations and enclosures", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201040000"), (short)4, "Test insulation resistance", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201050000"), (short)5, "Test ground continuity", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201060000"), (short)6, "Check fixtures, switches and receptacles", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201070000"), (short)7, "Label circuits and update the panel directory", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000201080000"), (short)8, "Restore power and confirm normal operation", new Guid("01a15175-3c00-7000-8000-000101000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202010000"), (short)1, "Lock out and tag out unit power", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202020000"), (short)2, "Replace return air filters", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202030000"), (short)3, "Check refrigerant pressures", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202040000"), (short)4, "Clean condenser coil", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202050000"), (short)5, "Inspect condenser fan motor and bearings", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202060000"), (short)6, "Check belts and pulleys", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202070000"), (short)7, "Clear condensate drain line", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202080000"), (short)8, "Record supply air temperature", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202090000"), (short)9, "Test thermostat and economizer controls", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000202100000"), (short)10, "Restore power and confirm normal operation", new Guid("01a15175-3c00-7000-8000-000102000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203010000"), (short)1, "Isolate gas and electrical supply", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203020000"), (short)2, "Inspect valves and fittings for leaks", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203030000"), (short)3, "Check system pressure", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203040000"), (short)4, "Test pressure relief valve", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203050000"), (short)5, "Inspect expansion tank", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203060000"), (short)6, "Check burner flame and combustion", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203070000"), (short)7, "Test low-water cutoff", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000203080000"), (short)8, "Restore supply and confirm operation", new Guid("01a15175-3c00-7000-8000-000103000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204010000"), (short)1, "Confirm each extinguisher is in its marked location", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204020000"), (short)2, "Check access is clear and signage is visible", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204030000"), (short)3, "Check the pressure gauge reads in the operable range", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204040000"), (short)4, "Inspect the pin, tamper seal and hose", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204050000"), (short)5, "Check for dents, corrosion or leakage", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204060000"), (short)6, "Weigh CO2 extinguishers", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000204070000"), (short)7, "Sign and date the inspection tag", new Guid("01a15175-3c00-7000-8000-000104000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205010000"), (short)1, "Measure line voltage", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205020000"), (short)2, "Measure load current", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205030000"), (short)3, "Check breaker temperatures under load", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205040000"), (short)4, "Isolate the panel and verify it is de-energized", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205050000"), (short)5, "Inspect the enclosure, cover and circuit directory", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205060000"), (short)6, "Check breakers for heat damage and correct ratings", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205070000"), (short)7, "Torque-check lugs and terminations", new Guid("01a15175-3c00-7000-8000-000105000000") },
                    { new Guid("01a15175-3c00-7000-8000-000205080000"), (short)8, "Restore power and confirm normal operation", new Guid("01a15175-3c00-7000-8000-000105000000") }
                });

            migrationBuilder.InsertData(
                schema: "fieldservice",
                table: "checklist_reading_fields",
                columns: new[] { "id", "checklist_item_id", "label", "position", "unit" },
                values: new object[,]
                {
                    { new Guid("01a15175-3c00-7000-8000-000301010100"), new Guid("01a15175-3c00-7000-8000-000201010000"), "Supply voltage", (short)1, "V" },
                    { new Guid("01a15175-3c00-7000-8000-000301040100"), new Guid("01a15175-3c00-7000-8000-000201040000"), "Insulation resistance", (short)1, "MΩ" },
                    { new Guid("01a15175-3c00-7000-8000-000301050100"), new Guid("01a15175-3c00-7000-8000-000201050000"), "Ground resistance", (short)1, "Ω" },
                    { new Guid("01a15175-3c00-7000-8000-000302030100"), new Guid("01a15175-3c00-7000-8000-000202030000"), "Suction", (short)1, "psi" },
                    { new Guid("01a15175-3c00-7000-8000-000302030200"), new Guid("01a15175-3c00-7000-8000-000202030000"), "Discharge", (short)2, "psi" },
                    { new Guid("01a15175-3c00-7000-8000-000302080100"), new Guid("01a15175-3c00-7000-8000-000202080000"), "Supply air temperature", (short)1, "°F" },
                    { new Guid("01a15175-3c00-7000-8000-000303030100"), new Guid("01a15175-3c00-7000-8000-000203030000"), "System pressure", (short)1, "psi" },
                    { new Guid("01a15175-3c00-7000-8000-000304060100"), new Guid("01a15175-3c00-7000-8000-000204060000"), "Gross weight", (short)1, "lb" },
                    { new Guid("01a15175-3c00-7000-8000-000305010100"), new Guid("01a15175-3c00-7000-8000-000205010000"), "Line to line", (short)1, "V" },
                    { new Guid("01a15175-3c00-7000-8000-000305010200"), new Guid("01a15175-3c00-7000-8000-000205010000"), "Line to neutral", (short)2, "V" },
                    { new Guid("01a15175-3c00-7000-8000-000305020100"), new Guid("01a15175-3c00-7000-8000-000205020000"), "Load current", (short)1, "A" },
                    { new Guid("01a15175-3c00-7000-8000-000305030100"), new Guid("01a15175-3c00-7000-8000-000205030000"), "Hottest breaker", (short)1, "°F" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_items_template_id_position",
                schema: "fieldservice",
                table: "checklist_items",
                columns: new[] { "template_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checklist_reading_fields_checklist_item_id_position",
                schema: "fieldservice",
                table: "checklist_reading_fields",
                columns: new[] { "checklist_item_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_name",
                schema: "fieldservice",
                table: "checklist_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspection_answers_checklist_item_id",
                schema: "fieldservice",
                table: "inspection_answers",
                column: "checklist_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_readings_checklist_item_id_reading_field_id",
                schema: "fieldservice",
                table: "inspection_readings",
                columns: new[] { "checklist_item_id", "reading_field_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_job_id",
                schema: "fieldservice",
                table: "inspections",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspections_technician_id",
                schema: "fieldservice",
                table: "inspections",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_history_actor_user_id",
                schema: "fieldservice",
                table: "job_history",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_history_job_id_occurred_at",
                schema: "fieldservice",
                table: "job_history",
                columns: new[] { "job_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_history_occurred_at",
                schema: "fieldservice",
                table: "job_history",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_job_history_technician_id",
                schema: "fieldservice",
                table: "job_history",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_assigned_technician_id_status",
                schema: "fieldservice",
                table: "jobs",
                columns: new[] { "assigned_technician_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_checklist_template_id",
                schema: "fieldservice",
                table: "jobs",
                column: "checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_created_by_user_id",
                schema: "fieldservice",
                table: "jobs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_number",
                schema: "fieldservice",
                table: "jobs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_scheduled_start",
                schema: "fieldservice",
                table: "jobs",
                column: "scheduled_start");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_updated_at_id",
                schema: "fieldservice",
                table: "jobs",
                columns: new[] { "status", "updated_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_updated_at_id",
                schema: "fieldservice",
                table: "jobs",
                columns: new[] { "updated_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_id",
                schema: "fieldservice",
                table: "refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                schema: "fieldservice",
                table: "refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "fieldservice",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                schema: "fieldservice",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "fieldservice",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.Sql("REVOKE INSERT, UPDATE, DELETE ON fieldservice.checklist_templates, fieldservice.checklist_items, fieldservice.checklist_reading_fields FROM fieldservice_app;");
            migrationBuilder.Sql("REVOKE INSERT, UPDATE, DELETE ON fieldservice.\"__EFMigrationsHistory\" FROM fieldservice_app;");
            migrationBuilder.Sql("GRANT SELECT ON fieldservice.\"__EFMigrationsHistory\" TO fieldservice_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON fieldservice.job_history FROM fieldservice_app;");
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fieldservice.advance_job_number() RETURNS bigint
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = fieldservice, pg_temp
                AS $fn$
                DECLARE
                    highest bigint;
                    next_number bigint;
                BEGIN
                    SELECT max(number) INTO highest FROM fieldservice.jobs;
                    SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END INTO next_number FROM fieldservice.jobs_number_seq;
                    IF highest >= next_number THEN
                        PERFORM setval('fieldservice.jobs_number_seq', highest);
                        next_number := highest + 1;
                    END IF;
                    RETURN next_number;
                END
                $fn$;
                """);
            migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION fieldservice.advance_job_number() FROM PUBLIC;");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION fieldservice.advance_job_number() TO fieldservice_app;");
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fieldservice.refuse_submitted_inspection_change() RETURNS trigger
                LANGUAGE plpgsql
                SECURITY INVOKER
                SET search_path = fieldservice, pg_temp
                AS $fn$
                BEGIN
                    IF NOT pg_has_role(current_user, 'fieldservice_migrator', 'MEMBER') THEN
                        IF TG_TABLE_NAME = 'inspections' THEN
                            IF OLD.submitted_at IS NOT NULL THEN
                                RAISE EXCEPTION 'submitted inspection % cannot change', OLD.id USING ERRCODE = '55000';
                            END IF;
                        ELSIF EXISTS (SELECT FROM fieldservice.inspections WHERE id = OLD.inspection_id AND submitted_at IS NOT NULL) THEN
                            RAISE EXCEPTION 'submitted inspection % cannot change', OLD.inspection_id USING ERRCODE = '55000';
                        END IF;
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END
                $fn$;
                """);
            migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION fieldservice.refuse_submitted_inspection_change() FROM PUBLIC;");
            migrationBuilder.Sql("CREATE TRIGGER tr_inspections_submitted_lock BEFORE UPDATE OR DELETE ON fieldservice.inspections FOR EACH ROW EXECUTE FUNCTION fieldservice.refuse_submitted_inspection_change();");
            migrationBuilder.Sql("CREATE TRIGGER tr_inspection_answers_submitted_lock BEFORE UPDATE OR DELETE ON fieldservice.inspection_answers FOR EACH ROW EXECUTE FUNCTION fieldservice.refuse_submitted_inspection_change();");
            migrationBuilder.Sql("CREATE TRIGGER tr_inspection_readings_submitted_lock BEFORE UPDATE OR DELETE ON fieldservice.inspection_readings FOR EACH ROW EXECUTE FUNCTION fieldservice.refuse_submitted_inspection_change();");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER tr_inspection_readings_submitted_lock ON fieldservice.inspection_readings;");
            migrationBuilder.Sql("DROP TRIGGER tr_inspection_answers_submitted_lock ON fieldservice.inspection_answers;");
            migrationBuilder.Sql("DROP TRIGGER tr_inspections_submitted_lock ON fieldservice.inspections;");
            migrationBuilder.Sql("DROP FUNCTION fieldservice.refuse_submitted_inspection_change();");
            migrationBuilder.Sql("DROP FUNCTION fieldservice.advance_job_number();");

            migrationBuilder.DropTable(
                name: "inspection_readings",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "job_history",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "checklist_reading_fields",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "inspection_answers",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "checklist_items",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "inspections",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "checklist_templates",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "technicians",
                schema: "fieldservice");

            migrationBuilder.DropTable(
                name: "users",
                schema: "fieldservice");
        }
    }
}

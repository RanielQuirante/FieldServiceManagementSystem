using System;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FieldService.Api.Persistence.Migrations
{
    [DbContext(typeof(FieldServiceDbContext))]
    [Migration("20261020094242_InitialSchema")]
    partial class InitialSchema
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("fieldservice")
                .HasAnnotation("ProductVersion", "10.0.13")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistItem", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<short>("Position")
                        .HasColumnType("smallint")
                        .HasColumnName("position");

                    b.Property<string>("Prompt")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("prompt");

                    b.Property<Guid>("TemplateId")
                        .HasColumnType("uuid")
                        .HasColumnName("template_id");

                    b.HasKey("Id")
                        .HasName("pk_checklist_items");

                    b.HasIndex("TemplateId", "Position")
                        .IsUnique()
                        .HasDatabaseName("ix_checklist_items_template_id_position");

                    b.ToTable("checklist_items", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_checklist_items_position", "position BETWEEN 1 AND 99");
                        });

                    b.HasData(
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201010000"),
                            Position = (short)1,
                            Prompt = "Measure supply voltage",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201020000"),
                            Position = (short)2,
                            Prompt = "Isolate and lock out the circuits being worked on",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201030000"),
                            Position = (short)3,
                            Prompt = "Inspect wiring, terminations and enclosures",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201040000"),
                            Position = (short)4,
                            Prompt = "Test insulation resistance",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201050000"),
                            Position = (short)5,
                            Prompt = "Test ground continuity",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201060000"),
                            Position = (short)6,
                            Prompt = "Check fixtures, switches and receptacles",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201070000"),
                            Position = (short)7,
                            Prompt = "Label circuits and update the panel directory",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000201080000"),
                            Position = (short)8,
                            Prompt = "Restore power and confirm normal operation",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000101000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202010000"),
                            Position = (short)1,
                            Prompt = "Lock out and tag out unit power",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202020000"),
                            Position = (short)2,
                            Prompt = "Replace return air filters",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202030000"),
                            Position = (short)3,
                            Prompt = "Check refrigerant pressures",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202040000"),
                            Position = (short)4,
                            Prompt = "Clean condenser coil",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202050000"),
                            Position = (short)5,
                            Prompt = "Inspect condenser fan motor and bearings",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202060000"),
                            Position = (short)6,
                            Prompt = "Check belts and pulleys",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202070000"),
                            Position = (short)7,
                            Prompt = "Clear condensate drain line",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202080000"),
                            Position = (short)8,
                            Prompt = "Record supply air temperature",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202090000"),
                            Position = (short)9,
                            Prompt = "Test thermostat and economizer controls",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000202100000"),
                            Position = (short)10,
                            Prompt = "Restore power and confirm normal operation",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000102000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203010000"),
                            Position = (short)1,
                            Prompt = "Isolate gas and electrical supply",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203020000"),
                            Position = (short)2,
                            Prompt = "Inspect valves and fittings for leaks",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203030000"),
                            Position = (short)3,
                            Prompt = "Check system pressure",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203040000"),
                            Position = (short)4,
                            Prompt = "Test pressure relief valve",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203050000"),
                            Position = (short)5,
                            Prompt = "Inspect expansion tank",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203060000"),
                            Position = (short)6,
                            Prompt = "Check burner flame and combustion",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203070000"),
                            Position = (short)7,
                            Prompt = "Test low-water cutoff",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000203080000"),
                            Position = (short)8,
                            Prompt = "Restore supply and confirm operation",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000103000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204010000"),
                            Position = (short)1,
                            Prompt = "Confirm each extinguisher is in its marked location",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204020000"),
                            Position = (short)2,
                            Prompt = "Check access is clear and signage is visible",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204030000"),
                            Position = (short)3,
                            Prompt = "Check the pressure gauge reads in the operable range",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204040000"),
                            Position = (short)4,
                            Prompt = "Inspect the pin, tamper seal and hose",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204050000"),
                            Position = (short)5,
                            Prompt = "Check for dents, corrosion or leakage",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204060000"),
                            Position = (short)6,
                            Prompt = "Weigh CO2 extinguishers",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000204070000"),
                            Position = (short)7,
                            Prompt = "Sign and date the inspection tag",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000104000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205010000"),
                            Position = (short)1,
                            Prompt = "Measure line voltage",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205020000"),
                            Position = (short)2,
                            Prompt = "Measure load current",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205030000"),
                            Position = (short)3,
                            Prompt = "Check breaker temperatures under load",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205040000"),
                            Position = (short)4,
                            Prompt = "Isolate the panel and verify it is de-energized",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205050000"),
                            Position = (short)5,
                            Prompt = "Inspect the enclosure, cover and circuit directory",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205060000"),
                            Position = (short)6,
                            Prompt = "Check breakers for heat damage and correct ratings",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205070000"),
                            Position = (short)7,
                            Prompt = "Torque-check lugs and terminations",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000205080000"),
                            Position = (short)8,
                            Prompt = "Restore power and confirm normal operation",
                            TemplateId = new Guid("01a15175-3c00-7000-8000-000105000000")
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistReadingField", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("ChecklistItemId")
                        .HasColumnType("uuid")
                        .HasColumnName("checklist_item_id");

                    b.Property<string>("Label")
                        .IsRequired()
                        .HasMaxLength(60)
                        .HasColumnType("character varying(60)")
                        .HasColumnName("label");

                    b.Property<short>("Position")
                        .HasColumnType("smallint")
                        .HasColumnName("position");

                    b.Property<string>("Unit")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("character varying(16)")
                        .HasColumnName("unit");

                    b.HasKey("Id")
                        .HasName("pk_checklist_reading_fields");

                    b.HasAlternateKey("ChecklistItemId", "Id")
                        .HasName("ak_checklist_reading_fields_checklist_item_id_id");

                    b.HasIndex("ChecklistItemId", "Position")
                        .IsUnique()
                        .HasDatabaseName("ix_checklist_reading_fields_checklist_item_id_position");

                    b.ToTable("checklist_reading_fields", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_checklist_reading_fields_position", "position BETWEEN 1 AND 9");
                        });

                    b.HasData(
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000301010100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000201010000"),
                            Label = "Supply voltage",
                            Position = (short)1,
                            Unit = "V"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000301040100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000201040000"),
                            Label = "Insulation resistance",
                            Position = (short)1,
                            Unit = "MΩ"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000301050100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000201050000"),
                            Label = "Ground resistance",
                            Position = (short)1,
                            Unit = "Ω"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000302030100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000202030000"),
                            Label = "Suction",
                            Position = (short)1,
                            Unit = "psi"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000302030200"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000202030000"),
                            Label = "Discharge",
                            Position = (short)2,
                            Unit = "psi"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000302080100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000202080000"),
                            Label = "Supply air temperature",
                            Position = (short)1,
                            Unit = "°F"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000303030100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000203030000"),
                            Label = "System pressure",
                            Position = (short)1,
                            Unit = "psi"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000304060100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000204060000"),
                            Label = "Gross weight",
                            Position = (short)1,
                            Unit = "lb"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000305010100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000205010000"),
                            Label = "Line to line",
                            Position = (short)1,
                            Unit = "V"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000305010200"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000205010000"),
                            Label = "Line to neutral",
                            Position = (short)2,
                            Unit = "V"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000305020100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000205020000"),
                            Label = "Load current",
                            Position = (short)1,
                            Unit = "A"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000305030100"),
                            ChecklistItemId = new Guid("01a15175-3c00-7000-8000-000205030000"),
                            Label = "Hottest breaker",
                            Position = (short)1,
                            Unit = "°F"
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistTemplate", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("name");

                    b.Property<string>("Trade")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("trade");

                    b.HasKey("Id")
                        .HasName("pk_checklist_templates");

                    b.HasIndex("Name")
                        .IsUnique()
                        .HasDatabaseName("ix_checklist_templates_name");

                    b.ToTable("checklist_templates", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_checklist_templates_trade", "trade IN ('Hvac', 'Electrical', 'Plumbing', 'FireSafety')");
                        });

                    b.HasData(
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000101000000"),
                            Name = "General electrical inspection",
                            Trade = "Electrical"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000102000000"),
                            Name = "Rooftop HVAC service",
                            Trade = "Hvac"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000103000000"),
                            Name = "Boiler service",
                            Trade = "Plumbing"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000104000000"),
                            Name = "Fire extinguisher inspection",
                            Trade = "FireSafety"
                        },
                        new
                        {
                            Id = new Guid("01a15175-3c00-7000-8000-000105000000"),
                            Name = "Electrical panel inspection",
                            Trade = "Electrical"
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.Inspection", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid>("JobId")
                        .HasColumnType("uuid")
                        .HasColumnName("job_id");

                    b.Property<DateTimeOffset?>("SubmittedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("submitted_at");

                    b.Property<Guid>("TechnicianId")
                        .HasColumnType("uuid")
                        .HasColumnName("technician_id");

                    b.Property<string>("TechnicianNotes")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("technician_notes");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id")
                        .HasName("pk_inspections");

                    b.HasIndex("JobId")
                        .IsUnique()
                        .HasDatabaseName("ix_inspections_job_id");

                    b.HasIndex("TechnicianId")
                        .HasDatabaseName("ix_inspections_technician_id");

                    b.ToTable("inspections", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_inspections_technician_notes", "technician_notes IS NULL OR (submitted_at IS NOT NULL AND btrim(technician_notes) <> '')");

                            t.HasCheckConstraint("ck_inspections_timestamps", "updated_at >= created_at AND (submitted_at IS NULL OR submitted_at >= created_at)");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.InspectionAnswer", b =>
                {
                    b.Property<Guid>("InspectionId")
                        .HasColumnType("uuid")
                        .HasColumnName("inspection_id");

                    b.Property<Guid>("ChecklistItemId")
                        .HasColumnType("uuid")
                        .HasColumnName("checklist_item_id");

                    b.Property<DateTimeOffset>("AnsweredAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("answered_at");

                    b.Property<string>("Note")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("note");

                    b.Property<string>("Result")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("result");

                    b.HasKey("InspectionId", "ChecklistItemId")
                        .HasName("pk_inspection_answers");

                    b.HasIndex("ChecklistItemId")
                        .HasDatabaseName("ix_inspection_answers_checklist_item_id");

                    b.ToTable("inspection_answers", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_inspection_answers_note", "(note IS NULL OR btrim(note) <> '') AND (result <> 'Fail' OR note IS NOT NULL)");

                            t.HasCheckConstraint("ck_inspection_answers_result", "result IN ('Pass', 'Fail', 'NotApplicable')");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.InspectionReading", b =>
                {
                    b.Property<Guid>("InspectionId")
                        .HasColumnType("uuid")
                        .HasColumnName("inspection_id");

                    b.Property<Guid>("ChecklistItemId")
                        .HasColumnType("uuid")
                        .HasColumnName("checklist_item_id");

                    b.Property<Guid>("ReadingFieldId")
                        .HasColumnType("uuid")
                        .HasColumnName("reading_field_id");

                    b.Property<decimal>("Value")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)")
                        .HasColumnName("value");

                    b.HasKey("InspectionId", "ChecklistItemId", "ReadingFieldId")
                        .HasName("pk_inspection_readings");

                    b.HasIndex("ChecklistItemId", "ReadingFieldId")
                        .HasDatabaseName("ix_inspection_readings_checklist_item_id_reading_field_id");

                    b.ToTable("inspection_readings", "fieldservice");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Job", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset?>("AssignedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("assigned_at");

                    b.Property<Guid?>("AssignedTechnicianId")
                        .HasColumnType("uuid")
                        .HasColumnName("assigned_technician_id");

                    b.Property<DateTimeOffset?>("CancelledAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("cancelled_at");

                    b.Property<Guid>("ChecklistTemplateId")
                        .HasColumnType("uuid")
                        .HasColumnName("checklist_template_id");

                    b.Property<DateTimeOffset?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.Property<string>("ContactName")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("contact_name");

                    b.Property<string>("ContactPhone")
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("contact_phone");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid>("CreatedByUserId")
                        .HasColumnType("uuid")
                        .HasColumnName("created_by_user_id");

                    b.Property<string>("CustomerName")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("customer_name");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("description");

                    b.Property<string>("Equipment")
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("equipment");

                    b.Property<long>("Number")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("number");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Number"));
                    NpgsqlPropertyBuilderExtensions.HasIdentityOptions(b.Property<long>("Number"), 1001L, null, null, null, null, null);

                    b.Property<string>("Priority")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("priority");

                    b.Property<DateTimeOffset>("ScheduledEnd")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("scheduled_end");

                    b.Property<DateTimeOffset>("ScheduledStart")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("scheduled_start");

                    b.Property<string>("SiteAccessNotes")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("site_access_notes");

                    b.Property<string>("SiteAddress")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("character varying(300)")
                        .HasColumnName("site_address");

                    b.Property<DateTimeOffset?>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("status");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("title");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id")
                        .HasName("pk_jobs");

                    b.HasIndex("ChecklistTemplateId")
                        .HasDatabaseName("ix_jobs_checklist_template_id");

                    b.HasIndex("CreatedByUserId")
                        .HasDatabaseName("ix_jobs_created_by_user_id");

                    b.HasIndex("Number")
                        .IsUnique()
                        .HasDatabaseName("ix_jobs_number");

                    b.HasIndex("ScheduledStart")
                        .HasDatabaseName("ix_jobs_scheduled_start");

                    b.HasIndex("AssignedTechnicianId", "Status")
                        .HasDatabaseName("ix_jobs_assigned_technician_id_status");

                    b.HasIndex("UpdatedAt", "Id")
                        .IsDescending()
                        .HasDatabaseName("ix_jobs_updated_at_id");

                    b.HasIndex("Status", "UpdatedAt", "Id")
                        .IsDescending(false, true, true)
                        .HasDatabaseName("ix_jobs_status_updated_at_id");

                    b.ToTable("jobs", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_jobs_assigned_at", "(assigned_technician_id IS NULL) = (assigned_at IS NULL)");

                            t.HasCheckConstraint("ck_jobs_assignee", "(status <> 'Unassigned' OR assigned_technician_id IS NULL) AND (status NOT IN ('Assigned', 'InProgress', 'OnHold', 'Completed') OR assigned_technician_id IS NOT NULL)");

                            t.HasCheckConstraint("ck_jobs_cancelled_at", "(status = 'Cancelled') = (cancelled_at IS NOT NULL)");

                            t.HasCheckConstraint("ck_jobs_completed_at", "(status = 'Completed') = (completed_at IS NOT NULL)");

                            t.HasCheckConstraint("ck_jobs_optional_text_not_blank", "(site_access_notes IS NULL OR btrim(site_access_notes) <> '') AND (contact_name IS NULL OR btrim(contact_name) <> '') AND (contact_phone IS NULL OR btrim(contact_phone) <> '') AND (equipment IS NULL OR btrim(equipment) <> '')");

                            t.HasCheckConstraint("ck_jobs_priority", "priority IN ('Low', 'Normal', 'High', 'Urgent')");

                            t.HasCheckConstraint("ck_jobs_required_text_not_blank", "btrim(title) <> '' AND btrim(description) <> '' AND btrim(customer_name) <> '' AND btrim(site_address) <> ''");

                            t.HasCheckConstraint("ck_jobs_schedule", "scheduled_end > scheduled_start");

                            t.HasCheckConstraint("ck_jobs_started_at", "CASE WHEN status IN ('InProgress', 'OnHold', 'Completed') THEN started_at IS NOT NULL WHEN status IN ('Unassigned', 'Assigned') THEN started_at IS NULL ELSE true END");

                            t.HasCheckConstraint("ck_jobs_status", "status IN ('Unassigned', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");

                            t.HasCheckConstraint("ck_jobs_timestamps", "updated_at >= created_at AND (completed_at IS NULL OR completed_at >= started_at)");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.JobHistoryEntry", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("action");

                    b.Property<Guid>("ActorUserId")
                        .HasColumnType("uuid")
                        .HasColumnName("actor_user_id");

                    b.Property<string>("FromStatus")
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("from_status");

                    b.Property<Guid>("JobId")
                        .HasColumnType("uuid")
                        .HasColumnName("job_id");

                    b.Property<string>("Note")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("note");

                    b.Property<DateTimeOffset>("OccurredAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("occurred_at");

                    b.Property<Guid?>("TechnicianId")
                        .HasColumnType("uuid")
                        .HasColumnName("technician_id");

                    b.Property<string>("ToStatus")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("to_status");

                    b.HasKey("Id")
                        .HasName("pk_job_history");

                    b.HasIndex("ActorUserId")
                        .HasDatabaseName("ix_job_history_actor_user_id");

                    b.HasIndex("OccurredAt")
                        .IsDescending()
                        .HasDatabaseName("ix_job_history_occurred_at");

                    b.HasIndex("TechnicianId")
                        .HasDatabaseName("ix_job_history_technician_id");

                    b.HasIndex("JobId", "OccurredAt")
                        .HasDatabaseName("ix_job_history_job_id_occurred_at");

                    b.ToTable("job_history", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_job_history_action", "action IN ('Created', 'Assigned', 'Started', 'PutOnHold', 'Resumed', 'Completed', 'Cancelled')");

                            t.HasCheckConstraint("ck_job_history_from_status", "from_status IN ('Unassigned', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");

                            t.HasCheckConstraint("ck_job_history_from_status_presence", "(action = 'Created') = (from_status IS NULL)");

                            t.HasCheckConstraint("ck_job_history_note", "CASE WHEN action IN ('PutOnHold', 'Cancelled') THEN note IS NOT NULL AND btrim(note) <> '' ELSE note IS NULL END");

                            t.HasCheckConstraint("ck_job_history_technician", "(action = 'Assigned') = (technician_id IS NOT NULL)");

                            t.HasCheckConstraint("ck_job_history_to_status", "to_status = CASE action WHEN 'Created' THEN 'Unassigned' WHEN 'Assigned' THEN 'Assigned' WHEN 'Started' THEN 'InProgress' WHEN 'PutOnHold' THEN 'OnHold' WHEN 'Resumed' THEN 'InProgress' WHEN 'Completed' THEN 'Completed' WHEN 'Cancelled' THEN 'Cancelled' END");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.RefreshToken", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset?>("ConsumedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("consumed_at");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid?>("ReplacedById")
                        .HasColumnType("uuid")
                        .HasColumnName("replaced_by_id");

                    b.Property<Guid>("SessionId")
                        .HasColumnType("uuid")
                        .HasColumnName("session_id");

                    b.Property<byte[]>("TokenHash")
                        .IsRequired()
                        .HasColumnType("bytea")
                        .HasColumnName("token_hash");

                    b.HasKey("Id")
                        .HasName("pk_refresh_tokens");

                    b.HasIndex("ReplacedById")
                        .HasDatabaseName("ix_refresh_tokens_replaced_by_id");

                    b.HasIndex("SessionId")
                        .HasDatabaseName("ix_refresh_tokens_session_id");

                    b.HasIndex("TokenHash")
                        .IsUnique()
                        .HasDatabaseName("ix_refresh_tokens_token_hash");

                    b.ToTable("refresh_tokens", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_refresh_tokens_replacement", "replaced_by_id IS NULL OR (consumed_at IS NOT NULL AND replaced_by_id <> id)");

                            t.HasCheckConstraint("ck_refresh_tokens_token_hash_length", "octet_length(token_hash) = 32");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.Technician", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<bool>("IsOnShift")
                        .HasColumnType("boolean")
                        .HasColumnName("is_on_shift");

                    b.Property<string>("PhoneNumber")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("phone_number");

                    b.Property<string>("Trade")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("trade");

                    b.HasKey("UserId")
                        .HasName("pk_technicians");

                    b.ToTable("technicians", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_technicians_phone_number_not_blank", "btrim(phone_number) <> ''");

                            t.HasCheckConstraint("ck_technicians_trade", "trade IN ('Hvac', 'Electrical', 'Plumbing', 'FireSafety')");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.User", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("DisplayName")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("display_name");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)")
                        .HasColumnName("email");

                    b.Property<bool>("IsActive")
                        .HasColumnType("boolean")
                        .HasColumnName("is_active");

                    b.Property<DateTimeOffset?>("LastSignedInAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_signed_in_at");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("password_hash");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("role");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id")
                        .HasName("pk_users");

                    b.HasIndex("Email")
                        .IsUnique()
                        .HasDatabaseName("ix_users_email");

                    b.ToTable("users", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_users_display_name_not_blank", "btrim(display_name) <> ''");

                            t.HasCheckConstraint("ck_users_email_format", "email LIKE '_%@_%'");

                            t.HasCheckConstraint("ck_users_email_lower_case", "email = lower(email)");

                            t.HasCheckConstraint("ck_users_role", "role IN ('Administrator', 'Technician')");

                            t.HasCheckConstraint("ck_users_updated_at", "updated_at >= created_at");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.UserSession", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("AbsoluteExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("absolute_expires_at");

                    b.Property<string>("Client")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("client");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<DateTimeOffset>("IdleExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("idle_expires_at");

                    b.Property<DateTimeOffset?>("LastRefreshedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_refreshed_at");

                    b.Property<DateTimeOffset?>("RevokedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("revoked_at");

                    b.Property<string>("RevokedReason")
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
                        .HasColumnName("revoked_reason");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.HasKey("Id")
                        .HasName("pk_user_sessions");

                    b.HasIndex("UserId")
                        .HasDatabaseName("ix_user_sessions_user_id");

                    b.ToTable("user_sessions", "fieldservice", t =>
                        {
                            t.HasCheckConstraint("ck_user_sessions_client", "client IN ('Web', 'Mobile')");

                            t.HasCheckConstraint("ck_user_sessions_expiry", "idle_expires_at > created_at AND absolute_expires_at > created_at AND idle_expires_at <= absolute_expires_at");

                            t.HasCheckConstraint("ck_user_sessions_last_refreshed_at", "last_refreshed_at IS NULL OR last_refreshed_at >= created_at");

                            t.HasCheckConstraint("ck_user_sessions_revocation", "(revoked_at IS NULL) = (revoked_reason IS NULL)");

                            t.HasCheckConstraint("ck_user_sessions_revoked_reason", "revoked_reason IN ('SignOut', 'RefreshTokenReuse', 'UserInactive', 'Operator')");
                        });
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistItem", b =>
                {
                    b.HasOne("FieldService.Api.Domain.ChecklistTemplate", null)
                        .WithMany("Items")
                        .HasForeignKey("TemplateId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_checklist_items_checklist_templates_template_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistReadingField", b =>
                {
                    b.HasOne("FieldService.Api.Domain.ChecklistItem", null)
                        .WithMany("ReadingFields")
                        .HasForeignKey("ChecklistItemId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_checklist_reading_fields_checklist_items_checklist_item_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Inspection", b =>
                {
                    b.HasOne("FieldService.Api.Domain.Job", null)
                        .WithOne()
                        .HasForeignKey("FieldService.Api.Domain.Inspection", "JobId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_inspections_jobs_job_id");

                    b.HasOne("FieldService.Api.Domain.Technician", null)
                        .WithMany()
                        .HasForeignKey("TechnicianId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_inspections_technicians_technician_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.InspectionAnswer", b =>
                {
                    b.HasOne("FieldService.Api.Domain.ChecklistItem", null)
                        .WithMany()
                        .HasForeignKey("ChecklistItemId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_inspection_answers_checklist_items_checklist_item_id");

                    b.HasOne("FieldService.Api.Domain.Inspection", null)
                        .WithMany("Answers")
                        .HasForeignKey("InspectionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_inspection_answers_inspections_inspection_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.InspectionReading", b =>
                {
                    b.HasOne("FieldService.Api.Domain.ChecklistReadingField", null)
                        .WithMany()
                        .HasForeignKey("ChecklistItemId", "ReadingFieldId")
                        .HasPrincipalKey("ChecklistItemId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_inspection_readings_checklist_reading_fields");

                    b.HasOne("FieldService.Api.Domain.InspectionAnswer", null)
                        .WithMany("Readings")
                        .HasForeignKey("InspectionId", "ChecklistItemId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_inspection_readings_inspection_answers");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Job", b =>
                {
                    b.HasOne("FieldService.Api.Domain.Technician", null)
                        .WithMany()
                        .HasForeignKey("AssignedTechnicianId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .HasConstraintName("fk_jobs_technicians_assigned_technician_id");

                    b.HasOne("FieldService.Api.Domain.ChecklistTemplate", null)
                        .WithMany()
                        .HasForeignKey("ChecklistTemplateId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_jobs_checklist_templates_checklist_template_id");

                    b.HasOne("FieldService.Api.Domain.User", null)
                        .WithMany()
                        .HasForeignKey("CreatedByUserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_jobs_users_created_by_user_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.JobHistoryEntry", b =>
                {
                    b.HasOne("FieldService.Api.Domain.User", null)
                        .WithMany()
                        .HasForeignKey("ActorUserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_job_history_users_actor_user_id");

                    b.HasOne("FieldService.Api.Domain.Job", null)
                        .WithMany("History")
                        .HasForeignKey("JobId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_job_history_jobs_job_id");

                    b.HasOne("FieldService.Api.Domain.Technician", null)
                        .WithMany()
                        .HasForeignKey("TechnicianId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .HasConstraintName("fk_job_history_technicians_technician_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.RefreshToken", b =>
                {
                    b.HasOne("FieldService.Api.Domain.RefreshToken", null)
                        .WithMany()
                        .HasForeignKey("ReplacedById")
                        .OnDelete(DeleteBehavior.SetNull)
                        .HasConstraintName("fk_refresh_tokens_refresh_tokens_replaced_by_id");

                    b.HasOne("FieldService.Api.Domain.UserSession", null)
                        .WithMany("RefreshTokens")
                        .HasForeignKey("SessionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_refresh_tokens_user_sessions_session_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Technician", b =>
                {
                    b.HasOne("FieldService.Api.Domain.User", "User")
                        .WithOne("Technician")
                        .HasForeignKey("FieldService.Api.Domain.Technician", "UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_technicians_users_user_id");

                    b.Navigation("User");
                });

            modelBuilder.Entity("FieldService.Api.Domain.UserSession", b =>
                {
                    b.HasOne("FieldService.Api.Domain.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_user_sessions_users_user_id");
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistItem", b =>
                {
                    b.Navigation("ReadingFields");
                });

            modelBuilder.Entity("FieldService.Api.Domain.ChecklistTemplate", b =>
                {
                    b.Navigation("Items");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Inspection", b =>
                {
                    b.Navigation("Answers");
                });

            modelBuilder.Entity("FieldService.Api.Domain.InspectionAnswer", b =>
                {
                    b.Navigation("Readings");
                });

            modelBuilder.Entity("FieldService.Api.Domain.Job", b =>
                {
                    b.Navigation("History");
                });

            modelBuilder.Entity("FieldService.Api.Domain.User", b =>
                {
                    b.Navigation("Technician");
                });

            modelBuilder.Entity("FieldService.Api.Domain.UserSession", b =>
                {
                    b.Navigation("RefreshTokens");
                });
#pragma warning restore 612, 618
        }
    }
}

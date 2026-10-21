using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace KukaMiddleware.Data;

public static class LegacyDatabaseUpgrade
{
    private const string UpgradeSql = """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Robots' AND column_name = 'Status')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Robots' AND column_name = 'StatusName') THEN
                ALTER TABLE "Robots" RENAME COLUMN "Status" TO "StatusName";
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Robots' AND column_name = 'LastUpdated')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Robots' AND column_name = 'UpdatedAt') THEN
                ALTER TABLE "Robots" RENAME COLUMN "LastUpdated" TO "UpdatedAt";
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Missions' AND column_name = 'Status')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Missions' AND column_name = 'StatusName') THEN
                ALTER TABLE "Missions" RENAME COLUMN "Status" TO "StatusName";
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'StatusHistories' AND column_name = 'Status')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'StatusHistories' AND column_name = 'StatusName') THEN
                ALTER TABLE "StatusHistories" RENAME COLUMN "Status" TO "StatusName";
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'StatusHistories' AND column_name = 'Timestamp')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'StatusHistories' AND column_name = 'ValidFrom') THEN
                ALTER TABLE "StatusHistories" RENAME COLUMN "Timestamp" TO "ValidFrom";
            END IF;
        END $$;

        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "RobotType" character varying(32);
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "StatusCode" smallint;
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "Availability" character varying(16);
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "BatteryLevel" numeric(5,2);
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "MapCode" character varying(32);
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "NodeCode" character varying(64);
        ALTER TABLE "Robots" ADD COLUMN IF NOT EXISTS "MissionCode" character varying(160);
        UPDATE "Robots"
        SET "StatusCode" = CASE UPPER("StatusName")
                WHEN 'DEPARTURE' THEN 1 WHEN 'OFFLINE' THEN 2
                WHEN 'AVAILABLE' THEN 3 WHEN 'IDLE' THEN 3
                WHEN 'BUSY' THEN 4 WHEN 'EXECUTING' THEN 4
                WHEN 'CHARGING' THEN 5 WHEN 'UPDATING' THEN 6
                WHEN 'ABNORMAL' THEN 7 ELSE NULL END
        WHERE "StatusCode" IS NULL;
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM "Robots" WHERE "StatusCode" IS NULL) THEN
                RAISE EXCEPTION 'Cannot migrate a robot with an unknown legacy status.';
            END IF;
        END $$;
        UPDATE "Robots"
        SET "StatusName" = CASE "StatusCode"
                WHEN 1 THEN 'Departure' WHEN 2 THEN 'Offline' WHEN 3 THEN 'Idle'
                WHEN 4 THEN 'Executing' WHEN 5 THEN 'Charging' WHEN 6 THEN 'Updating'
                WHEN 7 THEN 'Abnormal' END,
            "Availability" = CASE "StatusCode"
                WHEN 3 THEN 'Available' WHEN 1 THEN 'Busy' WHEN 4 THEN 'Busy'
                WHEN 5 THEN 'Busy' WHEN 6 THEN 'Busy' ELSE 'Offline' END;
        ALTER TABLE "Robots" ALTER COLUMN "StatusCode" SET NOT NULL;
        ALTER TABLE "Robots" ALTER COLUMN "StatusName" TYPE character varying(32);
        ALTER TABLE "Robots" ALTER COLUMN "StatusName" SET NOT NULL;
        ALTER TABLE "Robots" ALTER COLUMN "Availability" SET NOT NULL;

        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "RequestId" character varying(32);
        UPDATE "Missions"
        SET "RequestId" = md5(random()::text || clock_timestamp()::text)
        WHERE "RequestId" IS NULL;
        ALTER TABLE "Missions" ALTER COLUMN "RequestId" SET NOT NULL;
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "MissionType" character varying(20) NOT NULL DEFAULT 'RACK_MOVE';
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "StartNode" character varying(64) NOT NULL DEFAULT '';
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "TargetNode" character varying(64) NOT NULL DEFAULT '';
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "ContainerCode" character varying(64) NOT NULL DEFAULT '';
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "Priority" smallint NOT NULL DEFAULT 1;
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "StatusCode" smallint;
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone;
        ALTER TABLE "Missions" ADD COLUMN IF NOT EXISTS "CompletedAt" timestamp with time zone;
        UPDATE "Missions"
        SET "StatusCode" = CASE UPPER("StatusName")
                WHEN 'PENDING' THEN 10 WHEN 'CREATED' THEN 10
                WHEN 'EXECUTING' THEN 20 WHEN 'WAITING' THEN 25
                WHEN 'CANCELLING' THEN 28 WHEN 'CANCEL_REQUESTED' THEN 28
                WHEN 'COMPLETED' THEN 30 WHEN 'CANCELLED' THEN 31
                WHEN 'MANUAL_COMPLETED' THEN 35 WHEN 'WARNING' THEN 50
                WHEN 'STARTUP_ERROR' THEN 60 ELSE NULL END
        WHERE "StatusCode" IS NULL;
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM "Missions" WHERE "StatusCode" IS NULL) THEN
                RAISE EXCEPTION 'Cannot migrate a mission with an unknown legacy status.';
            END IF;
        END $$;
        UPDATE "Missions"
        SET "StatusName" = CASE "StatusCode"
                WHEN 10 THEN 'Created' WHEN 20 THEN 'Executing' WHEN 25 THEN 'Waiting'
                WHEN 28 THEN 'Cancelling' WHEN 30 THEN 'Completed' WHEN 31 THEN 'Cancelled'
                WHEN 35 THEN 'Manual completed' WHEN 50 THEN 'Warning' WHEN 60 THEN 'Startup error' END,
            "UpdatedAt" = COALESCE("UpdatedAt", "CreatedAt"),
            "CompletedAt" = CASE WHEN "StatusCode" IN (30, 31, 35, 60)
                THEN COALESCE("CompletedAt", "CreatedAt") ELSE NULL END;
        ALTER TABLE "Missions" ALTER COLUMN "StatusCode" SET NOT NULL;
        ALTER TABLE "Missions" ALTER COLUMN "StatusName" TYPE character varying(32);
        ALTER TABLE "Missions" ALTER COLUMN "StatusName" SET NOT NULL;
        ALTER TABLE "Missions" ALTER COLUMN "UpdatedAt" SET NOT NULL;

        ALTER TABLE "StatusHistories" ADD COLUMN IF NOT EXISTS "StatusCode" smallint;
        ALTER TABLE "StatusHistories" ADD COLUMN IF NOT EXISTS "ValidTo" timestamp with time zone;
        UPDATE "StatusHistories"
        SET "StatusCode" = CASE UPPER("StatusName")
                WHEN 'PENDING' THEN 10 WHEN 'CREATED' THEN 10
                WHEN 'EXECUTING' THEN 20 WHEN 'WAITING' THEN 25
                WHEN 'CANCELLING' THEN 28 WHEN 'CANCEL_REQUESTED' THEN 28
                WHEN 'COMPLETED' THEN 30 WHEN 'CANCELLED' THEN 31
                WHEN 'MANUAL_COMPLETED' THEN 35 WHEN 'WARNING' THEN 50
                WHEN 'STARTUP_ERROR' THEN 60 ELSE NULL END
        WHERE "StatusCode" IS NULL;
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM "StatusHistories" WHERE "StatusCode" IS NULL) THEN
                RAISE EXCEPTION 'Cannot migrate a mission history row with an unknown legacy status.';
            END IF;
        END $$;
        UPDATE "StatusHistories" AS old_history
        SET "StatusName" = CASE old_history."StatusCode"
                WHEN 10 THEN 'Created' WHEN 20 THEN 'Executing' WHEN 25 THEN 'Waiting'
                WHEN 28 THEN 'Cancelling' WHEN 30 THEN 'Completed' WHEN 31 THEN 'Cancelled'
                WHEN 35 THEN 'Manual completed' WHEN 50 THEN 'Warning' WHEN 60 THEN 'Startup error' END,
            "ValidTo" = next_state."NextValidFrom"
        FROM (
            SELECT "Id", LEAD("ValidFrom") OVER (PARTITION BY "MissionId" ORDER BY "ValidFrom") AS "NextValidFrom"
            FROM "StatusHistories"
        ) AS next_state
        WHERE old_history."Id" = next_state."Id";
        ALTER TABLE "StatusHistories" ALTER COLUMN "StatusCode" SET NOT NULL;
        ALTER TABLE "StatusHistories" ALTER COLUMN "StatusName" TYPE character varying(32);
        ALTER TABLE "StatusHistories" ALTER COLUMN "StatusName" SET NOT NULL;

        INSERT INTO "Robots" ("RobotId", "StatusCode", "StatusName", "Availability", "UpdatedAt")
        SELECT DISTINCT mission."RobotId", 2, 'Offline', 'Offline', CURRENT_TIMESTAMP
        FROM "Missions" AS mission
        WHERE NOT EXISTS (SELECT 1 FROM "Robots" AS robot WHERE robot."RobotId" = mission."RobotId");

        CREATE TABLE IF NOT EXISTS "RobotStatusHistories" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY,
            "RobotId" character varying(120) NOT NULL,
            "RobotType" character varying(32),
            "StatusCode" smallint NOT NULL,
            "StatusName" character varying(32) NOT NULL,
            "Availability" character varying(16) NOT NULL,
            "BatteryLevel" numeric(5,2),
            "MapCode" character varying(32),
            "NodeCode" character varying(64),
            "MissionCode" character varying(160),
            "ValidFrom" timestamp with time zone NOT NULL,
            "ValidTo" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_RobotStatusHistories" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_RobotStatusHistories_Robots_RobotId"
                FOREIGN KEY ("RobotId") REFERENCES "Robots" ("RobotId") ON DELETE CASCADE
        );

        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Missions_Robots_RobotId') THEN
                ALTER TABLE "Missions" ADD CONSTRAINT "FK_Missions_Robots_RobotId"
                    FOREIGN KEY ("RobotId") REFERENCES "Robots" ("RobotId") ON DELETE RESTRICT;
            END IF;
        END $$;
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Missions_MissionCode" ON "Missions" ("MissionCode");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Missions_RequestId" ON "Missions" ("RequestId");
        CREATE INDEX IF NOT EXISTS "IX_Missions_RobotId" ON "Missions" ("RobotId");
        CREATE INDEX IF NOT EXISTS "IX_StatusHistories_MissionId_ValidFrom"
            ON "StatusHistories" ("MissionId", "ValidFrom");
        CREATE INDEX IF NOT EXISTS "IX_RobotStatusHistories_RobotId_ValidTo"
            ON "RobotStatusHistories" ("RobotId", "ValidTo");

        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
            "MigrationId" character varying(150) NOT NULL,
            "ProductVersion" character varying(32) NOT NULL,
            CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
        );
        """;

    public static async Task UpgradeIfNeededAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (applied.Any() || !await HasLegacySchemaAsync(db, cancellationToken))
        {
            return;
        }

        var migrations = db.Database.GetMigrations().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(UpgradeSql, cancellationToken);
        foreach (var migration in migrations)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({migration}, {"8.0.11"}) ON CONFLICT ("MigrationId") DO NOTHING""",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> HasLegacySchemaAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            DbCommand command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT to_regclass('"Missions"') IS NOT NULL
                   AND to_regclass('"Robots"') IS NOT NULL
                   AND to_regclass('"StatusHistories"') IS NOT NULL
                """;
            await using (command)
            {
                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result is true;
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

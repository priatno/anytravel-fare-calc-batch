using Microsoft.Data.SqlClient;

// Nightly fare recalculation job. Originally set up as a cron entry on a
// standalone EC2 instance ("anytravel-batch-vm-01") — nobody on the current
// team has touched the scheduling since. Reads directly from BookingDB using
// the same connection string convention as booking-api, but this job predates
// that service and was never wired up to share config or logging.
//
// Usage: dotnet FareCalcBatch.dll
// Expects RDS_HOSTNAME / RDS_PORT / RDS_DB_NAME / RDS_USERNAME / RDS_PASSWORD
// as environment variables (same convention Elastic Beanstalk injects for
// booking-api; here they're set manually in the VM's crontab wrapper script).

var host = Environment.GetEnvironmentVariable("RDS_HOSTNAME") ?? "localhost";
var port = Environment.GetEnvironmentVariable("RDS_PORT") ?? "1433";
var db = Environment.GetEnvironmentVariable("RDS_DB_NAME") ?? "BookingDB";
var user = Environment.GetEnvironmentVariable("RDS_USERNAME") ?? "sa";
var password = Environment.GetEnvironmentVariable("RDS_PASSWORD") ?? "changeme";

var connectionString = $"Server={host},{port};Database={db};User Id={user};Password={password};TrustServerCertificate=True;";

Console.WriteLine($"[{DateTime.UtcNow:o}] fare-calc-batch starting");

using var conn = new SqlConnection(connectionString);
await conn.OpenAsync();

// Simple fare adjustment rule: bump fares 5% for flights departing within 7 days
// with fewer than expected bookings. Threshold values are hardcoded here, not
// in config — another item worth externalizing during modernization.
const decimal AdjustmentRate = 1.05m;

using var cmd = new SqlCommand(@"
    UPDATE Bookings
    SET FareAmount = FareAmount * @AdjustmentRate
    WHERE DepartureDate BETWEEN SYSUTCDATETIME() AND DATEADD(day, 7, SYSUTCDATETIME())
      AND Status = 'CONFIRMED'", conn);
cmd.Parameters.AddWithValue("@AdjustmentRate", AdjustmentRate);

var rowsAffected = await cmd.ExecuteNonQueryAsync();

Console.WriteLine($"[{DateTime.UtcNow:o}] fare-calc-batch complete. {rowsAffected} bookings adjusted.");

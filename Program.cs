using Azure;
using DbContextNamespace;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;

namespace EFvsSQLBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            int numberOfRuns = 50;
            List<long> nativeSqlJoinTimes = new List<long>();
            List<long> entityFrameworkSqlJoinTimes = new List<long>();

            List<long> nativeSqlInsertionTimesLarge = new List<long>();
            List<long> entityFrameworkInsertionTimesLarge = new List<long>();
            List<long> nativeSqlInsertionTimesSmall = new List<long>();
            List<long> entityFrameworkInsertionTimesSmall = new List<long>();

            List<long> nativeSqlReadingTimesLarge = new List<long>();
            List<long> entityFrameworkReadingTimesLarge = new List<long>();
            List<long> nativeSqlReadingTimesSmall = new List<long>();
            List<long> entityFrameworkReadingTimesSmall = new List<long>();

            for (int i = 1; i < numberOfRuns + 1; i++)
            {
                Console.WriteLine("RUN number " + i);

                string connectionString = "Server=;Initial Catalog=;Integrated Security=true;TrustServerCertificate=true"; // Fill in Initial catalog - db name, Server - likely your pc name, this connection string works with SSMS 19
                int startNumber = 16;
                int largeIncrement = 10000;
                int smallIncrement = 1000;

                // Feed JOIN DATA SCRIPT Each Superhero x10 superpowers in Connection tables x 5teams in superhero tables - 50rows per superhero
                // PRE REQUISITE - TABLE with 10 superpowers and 5 teams
                string sqlScript = @"
                    DECLARE @StartNumber INT = 16;
                    DECLARE @EndNumber INT = 5015; -- StartNumber + 5000

                    -- Generate sample data for Superheroes table
                    ;WITH SuperheroData AS (
                        SELECT TOP (@EndNumber - @StartNumber + 1)
                            SuperheroId = ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1,
                            Name = 'Superhero' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1 AS NVARCHAR(10)),
                            Alias = 'Alias' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1 AS NVARCHAR(10)),
                            Gender = CASE WHEN (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1) % 2 = 0 THEN 'Male' ELSE 'Female' END,
                            Alignment = CASE WHEN (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1) % 2 = 0 THEN 'Good' ELSE 'Evil' END,
                            Universe = CASE WHEN (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + @StartNumber - 1) % 3 = 0 THEN 'DC' ELSE 'Marvel' END
                        FROM master..spt_values
                    )
                    INSERT INTO Superheroes (SuperheroId, Name, Alias, Gender, Alignment, Universe)
                    SELECT SuperheroId, Name, Alias, Gender, Alignment, Universe
                    FROM SuperheroData;

                    -- Generate sample data for SuperheroPowers table
                    INSERT INTO SuperheroPowers (SuperheroId, PowerId)
                    SELECT SuperheroId, (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 10) + 1 -- Assuming you have 10 different powers
                    FROM Superheroes
                    CROSS JOIN (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) AS numbers(n)
                    WHERE SuperheroId BETWEEN @StartNumber AND @EndNumber
                    ORDER BY NEWID(); -- Shuffle the powers randomly for each superhero

                    -- Generate sample data for SuperheroTeams table
                    INSERT INTO SuperheroTeams (SuperheroId, TeamId)
                    SELECT SuperheroId, (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 5) + 1 -- Assuming you have 5 different teams
                    FROM Superheroes
                    CROSS JOIN (VALUES (0), (1), (2), (3), (4)) AS numbers(n)
                    WHERE SuperheroId BETWEEN @StartNumber AND @EndNumber
                    ORDER BY NEWID(); -- Shuffle the teams randomly for each superhero
                    ";

                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand(sqlScript, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                // Native SQL JOIN - Should return ~129450 rows
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var stopwatch = Stopwatch.StartNew();

                    var sqlQuery = @"
                        SELECT Superheroes.SuperheroId, Superheroes.Name AS SuperheroName, Superheroes.Alias, Superpowers.Name as Power, Teams.Name as TeamName
                        FROM Superheroes
                        INNER JOIN SuperheroPowers ON Superheroes.SuperheroId = SuperheroPowers.SuperheroId
                        INNER JOIN Superpowers ON SuperheroPowers.PowerId = Superpowers.PowerId
                        INNER JOIN SuperheroTeams ON Superheroes.SuperheroId = SuperheroTeams.SuperheroId
                        INNER JOIN Teams ON SuperheroTeams.TeamId = Teams.TeamId";

                    var superheroesWithPowersAndTeams = new List<(int SuperheroId, string SuperheroName, string Alias, string Power, string TeamName)>();
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var superheroId = (int)reader["SuperheroId"];
                                var superheroName = (string)reader["SuperheroName"];
                                var alias = (string)reader["Alias"];
                                var power = (string)reader["Power"];
                                var teamName = (string)reader["TeamName"];
                                superheroesWithPowersAndTeams.Add((superheroId, superheroName, alias, power, teamName));
                            }
                        }
                    }


                    stopwatch.Stop();
                    Console.WriteLine($"Native SQL Join Query Time: {stopwatch.ElapsedMilliseconds} ms");
                    Console.WriteLine("Native SQL Join result rows: " + superheroesWithPowersAndTeams.Count); //To check if there is the same amount of rows with other join
                    nativeSqlJoinTimes.Add(stopwatch.ElapsedMilliseconds);
                }

                //EF JOIN - Should return ~129450 rows
                using (var dbContext = new MyDbContext(connectionString))
                {
                    var stopwatch = Stopwatch.StartNew();

                    var superheroesWithPowersAndTeams = dbContext.Superheroes
                        .Join(
                            dbContext.SuperheroPowers,
                            superhero => superhero.SuperheroId,
                            superheroPower => superheroPower.SuperheroId,
                            (superhero, superheroPower) => new { Superhero = superhero, SuperheroPower = superheroPower })
                        .Join(
                            dbContext.Superpowers,
                            superJoin => superJoin.SuperheroPower.PowerId,
                            superpower => superpower.PowerId,
                            (superJoin, superpower) => new { SuperJoin = superJoin, Superpower = superpower })
                        .Join(
                            dbContext.SuperheroTeams,
                            superJoin => superJoin.SuperJoin.Superhero.SuperheroId,
                            superheroTeam => superheroTeam.SuperheroId,
                            (superJoin, superheroTeam) => new { SuperJoin = superJoin, SuperheroTeam = superheroTeam })
                        .Join(
                            dbContext.Teams,
                            superJoin => superJoin.SuperheroTeam.TeamId,
                            team => team.TeamId,
                            (superJoin, team) => new
                            {
                                SuperheroId = superJoin.SuperJoin.SuperJoin.Superhero.SuperheroId,
                                SuperheroName = superJoin.SuperJoin.SuperJoin.Superhero.Name,
                                Alias = superJoin.SuperJoin.SuperJoin.Superhero.Alias,
                                Power = superJoin.SuperJoin.Superpower.Name,
                                TeamName = team.Name
                            })
                        .ToList();

                    stopwatch.Stop();
                    Console.WriteLine($"Entity Framework Join Query Time: {stopwatch.ElapsedMilliseconds} ms");
                    Console.WriteLine("Native SQL Join result rows: " + superheroesWithPowersAndTeams.Count);  //To check if there is the same amount of rows with other join
                    Console.WriteLine();
                    entityFrameworkSqlJoinTimes.Add(stopwatch.ElapsedMilliseconds);
                }

                // CLEANUP after JOINS
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM SuperheroTeams";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM SuperheroPowers";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM Superheroes WHERE SuperheroId > 15";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }


                // WRITE LARGE DATA - NATIVE SQL

                using (var connection = new SqlConnection(connectionString))
                {
                    List<Superhero> superheroes = GenerateSampleData(startNumber, largeIncrement);
                    connection.Open();
                    var stopwatch = Stopwatch.StartNew();

                    // Create a DataTable to hold the data to be inserted
                    DataTable dataTable = new DataTable();
                    dataTable.Columns.Add("SuperheroId", typeof(int));
                    dataTable.Columns.Add("Name", typeof(string));
                    dataTable.Columns.Add("Alias", typeof(string));
                    dataTable.Columns.Add("Gender", typeof(string));
                    dataTable.Columns.Add("Alignment", typeof(string));
                    dataTable.Columns.Add("Universe", typeof(string));

                    // Populate the DataTable with superhero data
                    foreach (var superhero in superheroes)
                    {
                        dataTable.Rows.Add(superhero.SuperheroId, superhero.Name, superhero.Alias, superhero.Gender, superhero.Alignment, superhero.Universe);
                    }

                    // Use SqlBulkCopy to perform bulk insertion
                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, null))
                    {
                        bulkCopy.DestinationTableName = "Superheroes";
                        bulkCopy.WriteToServer(dataTable);
                    }

                    stopwatch.Stop();
                    Console.WriteLine($"Native SQL Insertion Time (Large Data): {stopwatch.ElapsedMilliseconds} ms");
                    nativeSqlInsertionTimesLarge.Add(stopwatch.ElapsedMilliseconds);
                }

                // DELETE DATA - NATIVE SQL
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM Superheroes WHERE SuperheroId > 15";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                // WRITE LARGE DATA - EF
                using (var dbContext = new MyDbContext(connectionString))
                {
                    List<Superhero> superheroes = GenerateSampleData(startNumber, largeIncrement);
                    var stopwatch = Stopwatch.StartNew();

                    dbContext.Superheroes.AddRange(superheroes);
                    dbContext.SaveChanges();

                    stopwatch.Stop();
                    Console.WriteLine($"Entity Framework Insertion Time (Large Data): {stopwatch.ElapsedMilliseconds} ms");
                    entityFrameworkInsertionTimesLarge.Add(stopwatch.ElapsedMilliseconds);
                }

                Console.WriteLine();

                // READ LARGE DATA - NATIVE SQL
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var stopwatch = Stopwatch.StartNew();

                    string sqlQuery = "SELECT SuperheroId, Name, Alias, Gender, Alignment, Universe FROM Superheroes";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Reading data...
                            }
                        }
                    }
                    stopwatch.Stop();
                    Console.WriteLine($"Native SQL Reading (Large Data): {stopwatch.ElapsedMilliseconds} ms");
                    nativeSqlReadingTimesLarge.Add(stopwatch.ElapsedMilliseconds);
                }

                // READ LARGE DATA - EF
                using (var dbContext = new MyDbContext(connectionString))
                {
                    var stopwatch = Stopwatch.StartNew();

                    var superheroes = dbContext.Superheroes.ToList();

                    stopwatch.Stop();
                    Console.WriteLine($"Entity Framework Reading (Large Data): {stopwatch.ElapsedMilliseconds} ms");
                    entityFrameworkReadingTimesLarge.Add(stopwatch.ElapsedMilliseconds);
                }

                // DELETE DATA - NATIVE SQL
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM Superheroes WHERE SuperheroId > 15";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                Console.WriteLine();

                // WRITE SMALL DATA - NATIVE SQL

                using (var connection = new SqlConnection(connectionString))
                {
                    List<Superhero> superheroes = GenerateSampleData(startNumber, smallIncrement);
                    connection.Open();
                    var stopwatch = Stopwatch.StartNew();

                    // Create a DataTable to hold the data to be inserted
                    DataTable dataTable = new DataTable();
                    dataTable.Columns.Add("SuperheroId", typeof(int));
                    dataTable.Columns.Add("Name", typeof(string));
                    dataTable.Columns.Add("Alias", typeof(string));
                    dataTable.Columns.Add("Gender", typeof(string));
                    dataTable.Columns.Add("Alignment", typeof(string));
                    dataTable.Columns.Add("Universe", typeof(string));

                    // Populate the DataTable with superhero data
                    foreach (var superhero in superheroes)
                    {
                        dataTable.Rows.Add(superhero.SuperheroId, superhero.Name, superhero.Alias, superhero.Gender, superhero.Alignment, superhero.Universe);
                    }

                    // Use SqlBulkCopy to perform bulk insertion
                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, null))
                    {
                        bulkCopy.DestinationTableName = "Superheroes";
                        bulkCopy.WriteToServer(dataTable);
                    }

                    stopwatch.Stop();
                    Console.WriteLine($"Native SQL Insertion Time (Small Data): {stopwatch.ElapsedMilliseconds} ms");
                    nativeSqlInsertionTimesSmall.Add(stopwatch.ElapsedMilliseconds);
                }

                // DELETE DATA - NATIVE SQL
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sqlQuery = "DELETE FROM Superheroes WHERE SuperheroId > 15";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                // WRITE SMALL DATA - EF
                using (var dbContext = new MyDbContext(connectionString))
                {
                    List<Superhero> superheroes = GenerateSampleData(startNumber, smallIncrement);
                    var stopwatch = Stopwatch.StartNew();

                    dbContext.Superheroes.AddRange(superheroes);
                    dbContext.SaveChanges();

                    stopwatch.Stop();
                    Console.WriteLine($"Entity Framework Insertion Time (Small Data): {stopwatch.ElapsedMilliseconds} ms");
                    entityFrameworkInsertionTimesSmall.Add(stopwatch.ElapsedMilliseconds);
                }

                Console.WriteLine();

                // READ SMALL DATA - NATIVE SQL
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var stopwatch = Stopwatch.StartNew();

                    string sqlQuery = "SELECT SuperheroId, Name, Alias, Gender, Alignment, Universe FROM Superheroes";
                    using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Reading data...
                            }
                        }
                    }
                    stopwatch.Stop();
                    Console.WriteLine($"Native SQL Reading (Small Data): {stopwatch.ElapsedMilliseconds} ms");
                    nativeSqlReadingTimesSmall.Add(stopwatch.ElapsedMilliseconds);
                }

                // READ SMALL DATA - EF
                using (var dbContext = new MyDbContext(connectionString))
                {
                    var stopwatch = Stopwatch.StartNew();

                    var superheroes = dbContext.Superheroes.ToList();

                    stopwatch.Stop();
                    Console.WriteLine($"Entity Framework Reading (Small Data): {stopwatch.ElapsedMilliseconds} ms");
                    entityFrameworkReadingTimesSmall.Add(stopwatch.ElapsedMilliseconds);
                }

                // DELETE DATA - EF
                using (var dbContext = new MyDbContext(connectionString))
                {
                    dbContext.Database.ExecuteSqlRaw("DELETE FROM Superheroes WHERE SuperheroId > 15");
                }

                Console.WriteLine();
            }

            Console.WriteLine("Total runs: " + numberOfRuns);
            Console.WriteLine();

            Console.WriteLine("Standard deviation:");
            Console.WriteLine();
            //Console.WriteLine("With Cold start");
            //Console.WriteLine();

            // Calculating Standard Deviation for Insertions
            //double nativeSqlInsertionStdDevLarge = CalculateStandardDeviation(nativeSqlInsertionTimesLarge);
            //double entityFrameworkInsertionStdDevLarge = CalculateStandardDeviation(entityFrameworkInsertionTimesLarge);
            //double nativeSqlInsertionStdDevSmall = CalculateStandardDeviation(nativeSqlInsertionTimesSmall);
            //double entityFrameworkInsertionStdDevSmall = CalculateStandardDeviation(entityFrameworkInsertionTimesSmall);

            //Console.WriteLine($"Native SQL Insertion Time (Large Data) - Std Dev: {nativeSqlInsertionStdDevLarge} ms");
            //Console.WriteLine($"Entity Framework Insertion Time (Large Data) - Std Dev: {entityFrameworkInsertionStdDevLarge} ms");
            //Console.WriteLine($"Native SQL Insertion Time (Small Data) - Std Dev: {nativeSqlInsertionStdDevSmall} ms");
            //Console.WriteLine($"Entity Framework Insertion Time (Small Data) - Std Dev: {entityFrameworkInsertionStdDevSmall} ms");

            //// Calculating Standard Deviation for Reads
            //double nativeSqlReadingStdDevLarge = CalculateStandardDeviation(nativeSqlReadingTimesLarge);
            //double entityFrameworkReadingStdDevLarge = CalculateStandardDeviation(entityFrameworkReadingTimesLarge);
            //double nativeSqlReadingStdDevSmall = CalculateStandardDeviation(nativeSqlReadingTimesSmall);
            //double entityFrameworkReadingStdDevSmall = CalculateStandardDeviation(entityFrameworkReadingTimesSmall);

            //Console.WriteLine($"Native SQL Reading Time (Large Data) - Std Dev: {nativeSqlReadingStdDevLarge} ms");
            //Console.WriteLine($"Entity Framework Reading Time (Large Data) - Std Dev: {entityFrameworkReadingStdDevLarge} ms");
            //Console.WriteLine($"Native SQL Reading Time (Small Data) - Std Dev: {nativeSqlReadingStdDevSmall} ms");
            //Console.WriteLine($"Entity Framework Reading Time (Small Data) - Std Dev: {entityFrameworkReadingStdDevSmall} ms");

            //Console.WriteLine("Without Cold start");
            //Console.WriteLine();

            // Calculating Standard Deviation for Joins
            double nativeSqlJoinStdDev = CalculateStandardDeviation(nativeSqlJoinTimes.Skip(1).ToList());
            double entityFrameworkJoinStdDev = CalculateStandardDeviation(entityFrameworkSqlJoinTimes.Skip(1).ToList());

            Console.WriteLine("JOIN OPERATIONS");
            Console.WriteLine($"Native SQL Join Time - Std Dev: {nativeSqlJoinStdDev} ms");
            Console.WriteLine($"Entity Framework Join Time - Std Dev: {entityFrameworkJoinStdDev} ms");

            // Calculating Standard Deviation for Insertions
            double nativeSqlInsertionStdDevLarge2 = CalculateStandardDeviation(nativeSqlInsertionTimesLarge.Skip(1).ToList());
            double entityFrameworkInsertionStdDevLarge2 = CalculateStandardDeviation(entityFrameworkInsertionTimesLarge.Skip(1).ToList());
            double nativeSqlInsertionStdDevSmall2 = CalculateStandardDeviation(nativeSqlInsertionTimesSmall.Skip(1).ToList());
            double entityFrameworkInsertionStdDevSmall2 = CalculateStandardDeviation(entityFrameworkInsertionTimesSmall.Skip(1).ToList());

            Console.WriteLine();

            Console.WriteLine("INSERT OPERATIONS");
            Console.WriteLine($"Native SQL Insertion Time (Large Data) - Std Dev: {nativeSqlInsertionStdDevLarge2} ms");
            Console.WriteLine($"Entity Framework Insertion Time (Large Data) - Std Dev: {entityFrameworkInsertionStdDevLarge2} ms");
            Console.WriteLine($"Native SQL Insertion Time (Small Data) - Std Dev: {nativeSqlInsertionStdDevSmall2} ms");
            Console.WriteLine($"Entity Framework Insertion Time (Small Data) - Std Dev: {entityFrameworkInsertionStdDevSmall2} ms");

            Console.WriteLine();

            // Calculating Standard Deviation for Reads
            double nativeSqlReadingStdDevLarge2 = CalculateStandardDeviation(nativeSqlReadingTimesLarge.Skip(1).ToList());
            double entityFrameworkReadingStdDevLarge2 = CalculateStandardDeviation(entityFrameworkReadingTimesLarge.Skip(1).ToList());
            double nativeSqlReadingStdDevSmall2 = CalculateStandardDeviation(nativeSqlReadingTimesSmall.Skip(1).ToList());
            double entityFrameworkReadingStdDevSmall2 = CalculateStandardDeviation(entityFrameworkReadingTimesSmall.Skip(1).ToList());

            Console.WriteLine("READ OPERATIONS");
            Console.WriteLine($"Native SQL Reading Time (Large Data) - Std Dev: {nativeSqlReadingStdDevLarge2} ms");
            Console.WriteLine($"Entity Framework Reading Time (Large Data) - Std Dev: {entityFrameworkReadingStdDevLarge2} ms");
            Console.WriteLine($"Native SQL Reading Time (Small Data) - Std Dev: {nativeSqlReadingStdDevSmall2} ms");
            Console.WriteLine($"Entity Framework Reading Time (Small Data) - Std Dev: {entityFrameworkReadingStdDevSmall2} ms");

            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("Average TIME:");
            Console.WriteLine();
            //Console.WriteLine("With Cold start");
            //Console.WriteLine();

            //// Calculating Avergae for Insertions
            //double nativeSqlInsertionAverageLarge = CalculateAverage(nativeSqlInsertionTimesLarge);
            //double entityFrameworkInsertionAverageLarge = CalculateAverage(entityFrameworkInsertionTimesLarge);
            //double nativeSqlInsertionAverageSmall = CalculateAverage(nativeSqlInsertionTimesSmall);
            //double entityFrameworkInsertionAverageSmall = CalculateAverage(entityFrameworkInsertionTimesSmall);

            //Console.WriteLine($"Native SQL Insertion Time (Large Data) - Average: {nativeSqlInsertionAverageLarge} ms");
            //Console.WriteLine($"Entity Framework Insertion Time (Large Data) - Average: {entityFrameworkInsertionAverageLarge} ms");
            //Console.WriteLine($"Native SQL Insertion Time (Small Data) - Average: {nativeSqlInsertionAverageSmall} ms");
            //Console.WriteLine($"Entity Framework Insertion Time (Small Data) - Average: {entityFrameworkInsertionAverageSmall} ms");

            //// Calculating Average for Reads
            //double nativeSqlReadingAverageLarge = CalculateAverage(nativeSqlReadingTimesLarge);
            //double entityFrameworkReadingAverageLarge = CalculateAverage(entityFrameworkReadingTimesLarge);
            //double nativeSqlReadingAverageSmall = CalculateAverage(nativeSqlReadingTimesSmall);
            //double entityFrameworkReadingAverageSmall = CalculateAverage(entityFrameworkReadingTimesSmall);

            //Console.WriteLine($"Native SQL Reading Time (Large Data) - Average: {nativeSqlReadingAverageLarge} ms");
            //Console.WriteLine($"Entity Framework Reading Time (Large Data) - Average: {entityFrameworkReadingAverageLarge} ms");
            //Console.WriteLine($"Native SQL Reading Time (Small Data) - Average: {nativeSqlReadingAverageSmall} ms");
            //Console.WriteLine($"Entity Framework Reading Time (Small Data) - Average: {entityFrameworkReadingAverageSmall} ms");

            //Console.WriteLine();

            //Console.WriteLine("Without Cold start");
            //Console.WriteLine();

            // Calculating Avergae for JOINS
            double nativeSqlJoinAverage = CalculateAverage(nativeSqlJoinTimes.Skip(1).ToList());
            double entityFrameworkJoinAverage = CalculateAverage(entityFrameworkSqlJoinTimes.Skip(1).ToList());

            Console.WriteLine("JOIN OPERATIONS");
            Console.WriteLine($"Native SQL Join Time - Average: {nativeSqlJoinAverage} ms");
            Console.WriteLine($"Entity Framework Join Time - Average: {entityFrameworkJoinAverage} ms");

            Console.WriteLine();

            // Calculating Avergae for Insertions
            double nativeSqlInsertionAverageLarge2 = CalculateAverage(nativeSqlInsertionTimesLarge.Skip(1).ToList());
            double entityFrameworkInsertionAverageLarge2 = CalculateAverage(entityFrameworkInsertionTimesLarge.Skip(1).ToList());
            double nativeSqlInsertionAverageSmall2 = CalculateAverage(nativeSqlInsertionTimesSmall.Skip(1).ToList());
            double entityFrameworkInsertionAverageSmall2 = CalculateAverage(entityFrameworkInsertionTimesSmall.Skip(1).ToList());

            Console.WriteLine("INSERT OPERATIONS");
            Console.WriteLine($"Native SQL Insertion Time (Large Data) - Average: {nativeSqlInsertionAverageLarge2} ms");
            Console.WriteLine($"Entity Framework Insertion Time (Large Data) - Average: {entityFrameworkInsertionAverageLarge2} ms");
            Console.WriteLine($"Native SQL Insertion Time (Small Data) - Average: {nativeSqlInsertionAverageSmall2} ms");
            Console.WriteLine($"Entity Framework Insertion Time (Small Data) - Average: {entityFrameworkInsertionAverageSmall2} ms");

            Console.WriteLine();

            // Calculating Average for Reads
            double nativeSqlReadingAverageLarge2 = CalculateAverage(nativeSqlReadingTimesLarge.Skip(1).ToList());
            double entityFrameworkReadingAverageLarge2 = CalculateAverage(entityFrameworkReadingTimesLarge.Skip(1).ToList());
            double nativeSqlReadingAverageSmall2 = CalculateAverage(nativeSqlReadingTimesSmall.Skip(1).ToList());
            double entityFrameworkReadingAverageSmall2 = CalculateAverage(entityFrameworkReadingTimesSmall.Skip(1).ToList());

            Console.WriteLine("READ OPERATIONS");
            Console.WriteLine($"Native SQL Reading Time (Large Data) - Average: {nativeSqlReadingAverageLarge2} ms");
            Console.WriteLine($"Entity Framework Reading Time (Large Data) - Average: {entityFrameworkReadingAverageLarge2} ms");
            Console.WriteLine($"Native SQL Reading Time (Small Data) - Average: {nativeSqlReadingAverageSmall2} ms");
            Console.WriteLine($"Entity Framework Reading Time (Small Data) - Average: {entityFrameworkReadingAverageSmall2} ms");

            Console.WriteLine();
        }

        //Sample Data Generatioin
        public static List<Superhero> GenerateSampleData(int start, int count)
        {
            var superheroes = new List<Superhero>();
            var random = new Random();

            string[] names = { "Spider-Man", "Iron Man", "Captain America", "Wonder Woman", "Superman", "Batman", "Thor", "Black Widow", "Hulk", "Aquaman" };
            string[] genders = { "Male", "Female" };
            string[] alignments = { "Hero", "Villain", "Neutral" };
            string[] universes = { "Marvel", "DC", "Other" };

            for (int i = start; i < count; i++)
            {
                var superhero = new Superhero
                {
                    SuperheroId = i,
                    Name = names[random.Next(names.Length)],
                    Alias = $"{names[random.Next(names.Length)]} {random.Next(1000)}", // Adding a random number to make aliases unique
                    Gender = genders[random.Next(genders.Length)],
                    Alignment = alignments[random.Next(alignments.Length)],
                    Universe = universes[random.Next(universes.Length)]
                };

                superheroes.Add(superhero);
            }

            return superheroes;
        }

        //Calculation of Standard Deviation
        public static double CalculateStandardDeviation(List<long> values)
        {
            double sum = values.Sum();
            double mean = sum / values.Count;
            double sumOfSquares = values.Select(x => Math.Pow(x - mean, 2)).Sum();
            double variance = sumOfSquares / (values.Count - 1);

            return Math.Sqrt(variance);
        }

        public static double CalculateAverage(List<long> values)
        {
            double sum = values.Sum();
            return sum / values.Count;
        }
    }
}

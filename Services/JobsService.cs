using CsvHelper;
using CsvHelper.Configuration;
using Grpc.Core;
using JobScraperService;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace JobScraperService.Services;

public class JobScraperServiceImpl : JobScraperService.JobScraperServiceBase
{
    private readonly ILogger<JobScraperServiceImpl> _logger;
    private readonly string _csvPath = "jobs.csv";

    public JobScraperServiceImpl(ILogger<JobScraperServiceImpl> logger)
    {
        _logger = logger;
    }

    // -------------------------- gRPC method --------------------------
    public override Task<JobProcessingResult> ProcessScrapedJobs(
        ScrapedJobBatch request, ServerCallContext context)
    {
        _logger.LogInformation(
            "Processing {Count} scraped jobs at {Time}",
            request.Jobs.Count, DateTime.UtcNow);

        // 1. read existing
        var existingJobs = ReadExistingJobs();

        // 2. diff
        var newJobs = FindNewJobs(request.Jobs, existingJobs);

        // 3. persist
        if (newJobs.Any())
        {
            AppendJobsToCsv(newJobs);
        }

        // 4. respond
        return Task.FromResult(new JobProcessingResult
        {
            NewJobsFound = newJobs.Count,
            NewJobTitles = { newJobs.Select(j => j.Title) }
        });
    }

    private List<ScrapedJob> ReadExistingJobs()
    {
        if (!File.Exists(_csvPath))
            return new();

        try
        {
            using var reader = new StreamReader(_csvPath);
            using var csv = new CsvReader(reader,
                new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    BadDataFound = null          // ignore badly‑formed rows
                });

            var records = csv.GetRecords<ScrapedJob>().ToList();
            _logger.LogInformation("Read {Count} existing jobs from CSV", records.Count);
            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading jobs from CSV");
            return new();
        }
    }

    private void AppendJobsToCsv(IEnumerable<ScrapedJob> jobs)
    {
        try
        {
            var fileExists = File.Exists(_csvPath);

            using var stream = File.Open(_csvPath,
                fileExists ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None);

            using var writer = new StreamWriter(stream);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            if (!fileExists)
            {
                csv.WriteHeader<ScrapedJob>();
                csv.NextRecord();
            }

            csv.WriteRecords(jobs);
            _logger.LogInformation("Successfully appended {Count} jobs to CSV", jobs.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing jobs to CSV");
            throw;
        }
    }

    private static List<ScrapedJob> FindNewJobs(IEnumerable<ScrapedJob> scrapedJobs,
                                                IEnumerable<ScrapedJob> existingJobs)
    {
        // Key by URL+Company — adjust if URLs aren’t unique
        var existingKeys = existingJobs
            .Select(j => $"{j.JobUrl}_{j.Company}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scrapedJobs
            .Where(j => !existingKeys.Contains($"{j.JobUrl}_{j.Company}"))
            .ToList();
    }
}

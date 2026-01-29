using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Triggers;

/// <summary>
/// Daily timer function that extends on-call schedules for all customers
/// to ensure schedules don't expire.
/// </summary>
public class ScheduleRegenerationTrigger
{
    private const int ScheduleHorizonDays = 45;

    private readonly ICustomerPlanBindingRepository _bindingRepository;
    private readonly IScheduleSliceRepository _sliceRepository;
    private readonly IOnCallScheduleService _scheduleService;
    private readonly ILogger<ScheduleRegenerationTrigger> _logger;

    public ScheduleRegenerationTrigger(
        ICustomerPlanBindingRepository bindingRepository,
        IScheduleSliceRepository sliceRepository,
        IOnCallScheduleService scheduleService,
        ILogger<ScheduleRegenerationTrigger> logger)
    {
        _bindingRepository = bindingRepository;
        _sliceRepository = sliceRepository;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    /// <summary>
    /// Runs daily at 2:00 AM UTC to extend on-call schedules.
    /// Only generates new days that don't exist yet.
    /// </summary>
    [Function(nameof(ExtendSchedules))]
    public async Task ExtendSchedules(
        [TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Starting daily schedule extension at {Time}", DateTime.UtcNow);

        var horizonUtc = DateTime.UtcNow.Date.AddDays(ScheduleHorizonDays);

        int extendedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        try
        {
            var bindings = await _bindingRepository.ListAsync(limit: 500);

            foreach (var binding in bindings)
            {
                try
                {
                    // Find the latest existing slice for this customer
                    var latestSlice = await _sliceRepository.GetLatestAsync(binding.CustomerId);

                    if (latestSlice == null)
                    {
                        // No slices exist, generate from today
                        var fromUtc = DateTime.UtcNow.Date;
                        await _scheduleService.RegenerateAsync(binding.CustomerId, fromUtc, horizonUtc);
                        extendedCount++;
                        _logger.LogInformation("Generated new schedule for customer {CustomerId} from {From} to {To}",
                            binding.CustomerId, fromUtc, horizonUtc);
                    }
                    else if (latestSlice.EndUtc < horizonUtc)
                    {
                        // Extend from where the existing schedule ends
                        var fromUtc = latestSlice.EndUtc.Date;
                        await _scheduleService.ExtendAsync(binding.CustomerId, fromUtc, horizonUtc);
                        extendedCount++;
                        _logger.LogDebug("Extended schedule for customer {CustomerId} from {From} to {To}",
                            binding.CustomerId, fromUtc, horizonUtc);
                    }
                    else
                    {
                        skippedCount++;
                        _logger.LogDebug("Schedule for customer {CustomerId} already extends to {End}, skipping",
                            binding.CustomerId, latestSlice.EndUtc);
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogError(ex, "Failed to extend schedule for customer {CustomerId}", binding.CustomerId);
                }
            }

            _logger.LogInformation(
                "Schedule extension completed. Extended: {ExtendedCount}, Skipped: {SkippedCount}, Errors: {ErrorCount}",
                extendedCount, skippedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete schedule extension");
            throw;
        }
    }
}

using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Helios365.Web.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Helios365.Web.Pages;

public partial class Resources : ComponentBase
{
    [Inject] private IResourceRepository ResourceRepository { get; set; } = default!;
    [Inject] private ICustomerRepository CustomerRepository { get; set; } = default!;
    [Inject] private IServicePrincipalRepository ServicePrincipalRepository { get; set; } = default!;
    [Inject] private IResourceDiscoveryService ResourceDiscoveryService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<Resources> Logger { get; set; } = default!;

    private List<ResourceViewModel> resourceViews = new();
    private bool isLoading = true;
    private bool syncing;
    private string search = string.Empty;
    private bool activeOnly;
    private string selectedCustomer = string.Empty;
    private string selectedServicePrincipal = string.Empty;
    private string selectedLocation = string.Empty;
    private List<KeyValuePair<string, string>> customerFilterOptions = new();
    private List<KeyValuePair<string, string>> servicePrincipalFilterOptions = new();
    private List<string> locationFilterOptions = new();

    private IEnumerable<ResourceViewModel> FilteredResources =>
        resourceViews
            .Where(r => !activeOnly || r.Resource.Active)
            .Where(r => string.IsNullOrEmpty(selectedCustomer) || string.Equals(r.Resource.CustomerId, selectedCustomer, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(selectedServicePrincipal) || string.Equals(r.Resource.ServicePrincipalId, selectedServicePrincipal, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(selectedLocation) || string.Equals(r.Location, selectedLocation, StringComparison.OrdinalIgnoreCase))
            .Where(r => MatchesSearch(r))
            .OrderByDescending(r => r.Resource.UpdatedAt);

    protected override async Task OnInitializedAsync()
    {
        await LoadResourcesAsync();
    }

    private Task ReloadAsync() => LoadResourcesAsync();

    private async Task SyncResourcesAsync()
    {
        syncing = true;
        try
        {
            var summary = await ResourceDiscoveryService.SyncAsync();
            Snackbar.Add($"Synced resources. Created: {summary.CreatedResources}, Updated: {summary.UpdatedResources}", Severity.Success);
            if (summary.Errors.Count > 0)
            {
                Snackbar.Add($"{summary.Errors.Count} errors encountered. See logs for details.", Severity.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync resources");
            Snackbar.Add($"Failed to sync resources: {ex.Message}", Severity.Error);
        }
        finally
        {
            syncing = false;
            await LoadResourcesAsync();
        }
    }

    private bool MatchesSearch(ResourceViewModel model)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        return (model.Resource.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || model.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(model.ServicePrincipalName) &&
                model.ServicePrincipalName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadResourcesAsync()
    {
        isLoading = true;
        try
        {
            var resources = (await ResourceRepository.ListAsync(limit: 2000)).ToList();
            var customers = (await CustomerRepository.ListAsync(limit: 2000)).ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);
            var servicePrincipals = (await ServicePrincipalRepository.ListAsync(limit: 2000)).ToDictionary(sp => sp.Id, sp => sp, StringComparer.OrdinalIgnoreCase);

            customerFilterOptions = customers
                .OrderBy(kvp => kvp.Value.Name)
                .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value.Name))
                .ToList();

            servicePrincipalFilterOptions = servicePrincipals
                .OrderBy(kvp => kvp.Value.Name)
                .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value.Name))
                .ToList();

            locationFilterOptions = resources
                .Select(r => TryGetMetadata(r, "location"))
                .Where(loc => loc is not "-" && !string.IsNullOrWhiteSpace(loc))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(loc => loc)
                .ToList();

            resourceViews = resources.Select(resource => new ResourceViewModel
            {
                Resource = resource,
                CustomerName = customers.TryGetValue(resource.CustomerId, out var customer) ? customer.Name : resource.CustomerId,
                ServicePrincipalName = (!string.IsNullOrWhiteSpace(resource.ServicePrincipalId) &&
                                       servicePrincipals.TryGetValue(resource.ServicePrincipalId, out var sp))
                    ? sp.Name
                    : resource.ServicePrincipalId,
                ResourceGroup = TryGetMetadata(resource, "resourceGroup"),
                Location = TryGetMetadata(resource, "location")
            })
            .OrderByDescending(r => r.Resource.UpdatedAt)
            .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load resources");
            Snackbar.Add($"Failed to load resources: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void ShowDetails(ResourceViewModel model)
    {
        var parameters = new DialogParameters
        {
            [nameof(ResourceDetailsDialog.Resource)] = model.Resource,
            [nameof(ResourceDetailsDialog.CustomerName)] = model.CustomerName,
            [nameof(ResourceDetailsDialog.ServicePrincipalName)] = model.ServicePrincipalName,
            [nameof(ResourceDetailsDialog.ResourceGroup)] = model.ResourceGroup,
            [nameof(ResourceDetailsDialog.Location)] = model.Location
        };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        DialogService.Show<ResourceDetailsDialog>("Resource details", parameters, options);
    }

    private async Task DeleteResourceAsync(ResourceViewModel model)
    {
        var confirmed = await DialogService.ShowMessageBox(
            title: "Remove resource",
            markupMessage: (MarkupString)$"Are you sure you want to remove '<strong>{model.Resource.Name}</strong>'?",
            yesText: "Remove",
            cancelText: "Cancel",
            options: new DialogOptions { MaxWidth = MaxWidth.ExtraSmall });

        if (confirmed == true)
        {
            try
            {
                var deleted = await ResourceRepository.DeleteAsync(model.Resource.Id);
                if (deleted)
                {
                    Snackbar.Add("Resource removed.", Severity.Success);
                    await LoadResourcesAsync();
                }
                else
                {
                    Snackbar.Add("Resource not found.", Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to delete resource {ResourceId}", model.Resource.Id);
                Snackbar.Add($"Failed to delete resource: {ex.Message}", Severity.Error);
            }
        }
    }

    private static string TryGetMetadata(Resource resource, string key)
    {
        if (resource.Metadata is null)
        {
            return "-";
        }

        foreach (var kvp in resource.Metadata)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(kvp.Value) ? "-" : kvp.Value;
            }
        }

        return "-";
    }

    private string GetResourceImage(string resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return "images/resources/generic.svg";
        }

        var type = resourceType.ToLowerInvariant();
        if (type.Contains("microsoft.web/sites"))
        {
            return "images/resources/appservice.svg";
        }

        if (type.Contains("microsoft.compute/virtualmachines"))
        {
            return "images/resources/vm.svg";
        }

        if (type.Contains("servicebus"))
        {
            return "images/resources/servicebus.svg";
        }

        if (type.Contains("dbformysql"))
        {
            return "images/resources/database.svg";
        }

        return "images/resources/generic.svg";
    }

    private sealed class ResourceViewModel
    {
        public required Resource Resource { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public string ServicePrincipalName { get; init; } = string.Empty;
        public string ResourceGroup { get; init; } = "-";
        public string Location { get; init; } = "-";
    }
}

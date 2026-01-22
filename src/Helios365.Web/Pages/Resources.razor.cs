using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Helios365.Web.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace Helios365.Web.Pages;

public partial class Resources : ComponentBase
{
    [Inject] private IResourceRepository ResourceRepository { get; set; } = default!;
    [Inject] private ICustomerRepository CustomerRepository { get; set; } = default!;
    [Inject] private IServicePrincipalRepository ServicePrincipalRepository { get; set; } = default!;
    [Inject] private IResourceSyncService ResourceSyncService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<Resources> Logger { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private MudTable<ResourceViewModel>? table;
    private List<ResourceViewModel> resourceViews = new();
    private bool isLoading = true;
    private bool syncing;
    private string search = string.Empty;
    private string selectedCustomer = string.Empty;
    private string selectedServicePrincipal = string.Empty;
    private string selectedLocation = string.Empty;
    private string selectedResourceType = string.Empty;
    private int currentPage = 0;
    private int pageSize = 20;
    private bool pageInitialized;
    private List<KeyValuePair<string, string>> customerFilterOptions = new();
    private List<KeyValuePair<string, string>> servicePrincipalFilterOptions = new();
    private List<string> locationFilterOptions = new();
    private List<string> resourceTypeOptions = new();

    private IEnumerable<ResourceViewModel> FilteredResources =>
        resourceViews
            .Where(r => string.IsNullOrEmpty(selectedCustomer) || string.Equals(r.Resource.CustomerId, selectedCustomer, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(selectedServicePrincipal) || string.Equals(r.Resource.ServicePrincipalId, selectedServicePrincipal, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(selectedLocation) || string.Equals(r.Location, selectedLocation, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(selectedResourceType) || string.Equals(r.Resource.ResourceType, selectedResourceType, StringComparison.OrdinalIgnoreCase))
            .Where(r => MatchesSearch(r))
            .OrderByDescending(r => r.Resource.UpdatedAt);

    private Task<TableData<ResourceViewModel>> LoadPageAsync(TableState state, CancellationToken _ = default)
    {
        var page = pageInitialized ? state.Page : currentPage;
        var size = pageInitialized ? state.PageSize : pageSize;

        currentPage = page;
        pageSize = size;
        UpdateQueryUrl();

        var data = FilteredResources.Skip(page * size).Take(size).ToList();
        var total = FilteredResources.Count();
        pageInitialized = true;
        return Task.FromResult(new TableData<ResourceViewModel>
        {
            Items = data,
            TotalItems = total
        });
    }

    private Task OnSearchChanged(string value)
    {
        search = value;
        currentPage = 0;
        UpdateQueryUrl();
        return ReloadTableAsync();
    }

    private Task OnCustomerChanged(string value)
    {
        selectedCustomer = value;
        currentPage = 0;
        UpdateQueryUrl();
        return ReloadTableAsync();
    }

    private Task OnServicePrincipalChanged(string value)
    {
        selectedServicePrincipal = value;
        currentPage = 0;
        UpdateQueryUrl();
        return ReloadTableAsync();
    }

    private Task OnLocationChanged(string value)
    {
        selectedLocation = value;
        currentPage = 0;
        UpdateQueryUrl();
        return ReloadTableAsync();
    }

    private Task OnResourceTypeChanged(string value)
    {
        selectedResourceType = value;
        currentPage = 0;
        UpdateQueryUrl();
        return ReloadTableAsync();
    }

    private Task ReloadTableAsync() => table?.ReloadServerData() ?? Task.CompletedTask;

    protected override async Task OnInitializedAsync()
    {
        LoadStateFromQuery();
        await LoadResourcesAsync();
    }

    private Task ReloadAsync() => LoadResourcesAsync();

    private async Task SyncResourcesAsync()
    {
        syncing = true;
        try
        {
            var summary = await ResourceSyncService.SyncAsync();
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

            resourceTypeOptions = resources
                .Select(r => r.ResourceType)
                .Where(rt => !string.IsNullOrWhiteSpace(rt))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(rt => rt)
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
            await ReloadTableAsync();
        }
    }

    private void LoadStateFromQuery()
    {
        var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);

        if (query.TryGetValue("search", out var searchParam))
        {
            search = searchParam.ToString();
        }
        if (query.TryGetValue("customer", out var customerParam))
        {
            selectedCustomer = customerParam.ToString();
        }
        if (query.TryGetValue("sp", out var spParam))
        {
            selectedServicePrincipal = spParam.ToString();
        }
        if (query.TryGetValue("location", out var locParam))
        {
            selectedLocation = locParam.ToString();
        }
        if (query.TryGetValue("type", out var typeParam))
        {
            selectedResourceType = typeParam.ToString();
        }
        if (query.TryGetValue("page", out var pageParam) && int.TryParse(pageParam, out var parsedPage) && parsedPage >= 0)
        {
            currentPage = parsedPage;
        }
        if (query.TryGetValue("pageSize", out var sizeParam) && int.TryParse(sizeParam, out var parsedSize) && parsedSize > 0)
        {
            pageSize = parsedSize;
        }
    }

    private void UpdateQueryUrl()
    {
        var baseUri = $"{NavigationManager.BaseUri.TrimEnd('/')}/resources";
        var parameters = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters["search"] = search;
        }
        if (!string.IsNullOrWhiteSpace(selectedCustomer))
        {
            parameters["customer"] = selectedCustomer;
        }
        if (!string.IsNullOrWhiteSpace(selectedServicePrincipal))
        {
            parameters["sp"] = selectedServicePrincipal;
        }
        if (!string.IsNullOrWhiteSpace(selectedLocation))
        {
            parameters["location"] = selectedLocation;
        }
        if (!string.IsNullOrWhiteSpace(selectedResourceType))
        {
            parameters["type"] = selectedResourceType;
        }

        parameters["page"] = currentPage.ToString();
        parameters["pageSize"] = pageSize.ToString();

        var newUri = QueryHelpers.AddQueryString(baseUri, parameters);
        if (!string.Equals(newUri, NavigationManager.Uri, StringComparison.OrdinalIgnoreCase))
        {
            NavigationManager.NavigateTo(newUri, replace: true);
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
            return "/images/resources/generic.svg";
        }

        var type = resourceType.ToLowerInvariant();
        if (type.Contains("microsoft.web/sites"))
        {
            return "/images/resources/appservice.svg";
        }

        if (type.Contains("microsoft.compute/virtualmachines"))
        {
            return "/images/resources/vm.svg";
        }

        if (type.Contains("servicebus"))
        {
            return "/images/resources/servicebus.svg";
        }

        if (type.Contains("dbformysql"))
        {
            return "/images/resources/database.svg";
        }

        return "/images/resources/generic.svg";
    }

    private void HandleRowClick(TableRowClickEventArgs<ResourceViewModel> args)
    {
        NavigateToDetails(args.Item.Resource.Id);
    }

    private async void NavigateToDetails(string? resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            await JSRuntime.InvokeVoidAsync("eval", "history.scrollRestoration = 'manual'");
            NavigationManager.NavigateTo($"/resources/{resourceId}", forceLoad: false);
        }
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

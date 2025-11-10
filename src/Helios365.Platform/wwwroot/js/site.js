// ===== HELIOS365 MAIN JAVASCRIPT =====

document.addEventListener('DOMContentLoaded', function() {
    // Initialize tooltips
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Initialize popovers
    var popoverTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="popover"]'));
    var popoverList = popoverTriggerList.map(function (popoverTriggerEl) {
        return new bootstrap.Popover(popoverTriggerEl);
    });

    // Auto-refresh functionality for dashboard
    const autoRefreshElements = document.querySelectorAll('[data-auto-refresh]');
    autoRefreshElements.forEach(element => {
        const interval = parseInt(element.dataset.autoRefresh) * 1000;
        if (interval > 0) {
            setInterval(() => {
                location.reload();
            }, interval);
        }
    });

    // Smooth animations for stat cards
    const statCards = document.querySelectorAll('.stat-card');
    statCards.forEach((card, index) => {
        card.style.animationDelay = `${index * 0.1}s`;
        card.classList.add('fade-in-up');
    });

    // Real-time updates (placeholder for future WebSocket integration)
    window.Helios365 = {
        // Utility functions
        formatDate: function(dateString) {
            const date = new Date(dateString);
            return date.toLocaleString();
        },
        
        // Status badge helper
        getStatusBadgeClass: function(status) {
            const statusMap = {
                'Received': 'bg-secondary',
                'Routing': 'bg-info',
                'Checking': 'bg-info',
                'Remediating': 'bg-warning text-dark',
                'Rechecking': 'bg-info',
                'Resolved': 'bg-success',
                'Healthy': 'bg-success',
                'Escalated': 'bg-danger',
                'Failed': 'bg-danger'
            };
            return statusMap[status] || 'bg-secondary';
        },

        // Severity badge helper
        getSeverityBadgeClass: function(severity) {
            const severityMap = {
                'Critical': 'bg-danger',
                'High': 'bg-warning text-dark',
                'Medium': 'bg-info',
                'Low': 'bg-secondary',
                'Info': 'bg-light text-dark'
            };
            return severityMap[severity] || 'bg-secondary';
        },

        // Future: WebSocket connection for real-time updates
        connect: function() {
            // TODO: Implement SignalR/WebSocket connection
            console.log('Helios365 real-time connection initialized');
        }
    };

    // Initialize Helios365
    window.Helios365.connect();
});

// Utility functions for form validation and UX
function showNotification(message, type = 'info', duration = 5000) {
    const alertDiv = document.createElement('div');
    alertDiv.className = `alert alert-${type} alert-dismissible fade show position-fixed`;
    alertDiv.style.cssText = 'top: 20px; right: 20px; z-index: 9999; min-width: 300px;';
    alertDiv.innerHTML = `
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    `;
    
    document.body.appendChild(alertDiv);
    
    // Auto-remove after duration
    setTimeout(() => {
        if (alertDiv && alertDiv.parentNode) {
            alertDiv.remove();
        }
    }, duration);
}

// Loading state helpers
function showLoading(element) {
    element.classList.add('loading');
    element.disabled = true;
}

function hideLoading(element) {
    element.classList.remove('loading');
    element.disabled = false;
}
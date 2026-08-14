// Shared Chart.js renderer for the Statistics page's stat cards. Replaces the previous
// per-card eval()'d script-string approach with a single real function, so a fix here
// applies to every stat card instead of needing to be copy-pasted into four places.
window.rrsmStatsCharts = window.rrsmStatsCharts || {};

window.renderStatsChart = function (canvasId, labels, values, options) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (window.rrsmStatsCharts[canvasId]) {
        window.rrsmStatsCharts[canvasId].destroy();
    }

    const unit = options.unit || '';
    const decimals = options.decimals ?? 1;
    const formatValue = (value) => value.toFixed(decimals) + (unit ? ' ' + unit : '');

    window.rrsmStatsCharts[canvasId] = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: options.label,
                data: values,
                borderColor: options.borderColor,
                backgroundColor: options.backgroundColor,
                tension: 0.3,
                fill: true,
                pointRadius: 0,
                pointHoverRadius: 4,
                pointHoverBackgroundColor: options.borderColor,
                pointHoverBorderColor: '#1e293b',
                pointHoverBorderWidth: 2,
                borderWidth: 2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                tooltip: {
                    mode: 'index',
                    intersect: false,
                    backgroundColor: 'rgba(30, 41, 59, 0.95)',
                    borderColor: 'rgba(71, 85, 105, 0.5)',
                    borderWidth: 1,
                    titleColor: '#94a3b8',
                    bodyColor: '#fff',
                    titleFont: { size: 11 },
                    bodyFont: { size: 12, weight: 'bold' },
                    padding: 8,
                    cornerRadius: 8,
                    callbacks: {
                        label: function (context) {
                            return formatValue(context.parsed.y);
                        }
                    }
                }
            },
            scales: {
                x: {
                    ticks: { color: '#64748b', font: { size: 10 } },
                    grid: { color: 'rgba(71, 85, 105, 0.2)' },
                    border: { display: false }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: '#64748b',
                        font: { size: 10 },
                        stepSize: options.stepSize || undefined,
                        callback: function (value) {
                            return formatValue(value);
                        }
                    },
                    grid: { color: 'rgba(71, 85, 105, 0.2)' },
                    border: { display: false }
                }
            }
        }
    });
};

window.destroyStatsChart = function (canvasId) {
    if (window.rrsmStatsCharts[canvasId]) {
        window.rrsmStatsCharts[canvasId].destroy();
        delete window.rrsmStatsCharts[canvasId];
    }
};

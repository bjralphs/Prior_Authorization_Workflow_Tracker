// PA Tracker — client-side helpers

// Chart.js wrapper — creates or replaces a chart on a canvas element.
// Called from Blazor via IJSRuntime after each data load.
window.paCharts = {};
window.renderChart = function (canvasId, config) {
    if (window.paCharts[canvasId]) {
        window.paCharts[canvasId].destroy();
        delete window.paCharts[canvasId];
    }
    const canvas = document.getElementById(canvasId);
    if (!canvas || typeof Chart === 'undefined') return;
    window.paCharts[canvasId] = new Chart(canvas, config);
};

/**
 * Triggers a browser file download from a byte array returned by the server.
 * Used by the CSV export feature (FR-010).
 * @param {string} fileName  - e.g. "PA_Export_20260504_120000.csv"
 * @param {string} mimeType  - e.g. "text/csv"
 * @param {Uint8Array} bytes - file bytes from .NET byte[]
 */
window.downloadFileFromBytes = function (fileName, mimeType, bytes) {
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

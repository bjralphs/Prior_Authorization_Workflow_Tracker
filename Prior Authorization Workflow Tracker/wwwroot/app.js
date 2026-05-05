// PA Tracker — client-side helpers

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

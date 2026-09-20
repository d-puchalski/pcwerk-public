const reconnectOverlay = document.getElementById("components-reconnect-modal");

reconnectOverlay.addEventListener("components-reconnect-state-changed", async event => {
    if (event.detail.state === "failed" || event.detail.state === "rejected") {
        location.reload();
        return;
    }

    if (event.detail.state === "paused") {
        try {
            const resumed = await Blazor.resumeCircuit();

            if (!resumed) {
                location.reload();
            }
        } catch {
            location.reload();
        }
    }
});

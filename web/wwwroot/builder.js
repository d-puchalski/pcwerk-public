window.pcBuilder = {
    shouldOpenModal: function () {
        const url = new URL(window.location.href);
        return url.hash.toLowerCase() === "#configurator"
            || url.searchParams.has("build")
            || url.searchParams.get("configurator") === "true";
    },
    setModalOpen: function (isOpen, dialogId) {
        document.querySelectorAll(".page-shell").forEach(function (shell) {
            shell.inert = isOpen;
        });

        if (isOpen) {
            window.pcBuilder.modalTrigger = document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null;
            if (document.body.dataset.pcwerkOverflow === undefined) {
                document.body.dataset.pcwerkOverflow = document.body.style.overflow || "";
            }
            document.body.style.overflow = "hidden";
            window.setTimeout(function () {
                document.getElementById(dialogId)?.focus({ preventScroll: true });
            }, 0);
            return;
        }

        const previousOverflow = document.body.dataset.pcwerkOverflow;
        if (previousOverflow !== undefined) {
            document.body.style.overflow = previousOverflow;
            delete document.body.dataset.pcwerkOverflow;
        }

        if (window.pcBuilder.modalTrigger?.isConnected) {
            window.pcBuilder.modalTrigger.focus({ preventScroll: true });
        }
        window.pcBuilder.modalTrigger = null;
    },
    save: function (state) {
        localStorage.setItem("pcwerk-build", state);
    },
    share: async function (state) {
        const bytes = new TextEncoder().encode(state);
        const value = btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
        const url = new URL(window.location.href);
        url.searchParams.set("build", value);
        url.searchParams.set("configurator", "true");
        url.hash = "";
        await navigator.clipboard.writeText(url.toString());
    },
    loadInitialBuild: function () {
        const value = new URL(window.location.href).searchParams.get("build");
        if (value) {
            try {
                const normalized = value.replaceAll("-", "+").replaceAll("_", "/");
                const bytes = Uint8Array.from(atob(normalized), c => c.charCodeAt(0));
                return new TextDecoder().decode(bytes);
            } catch {
                return null;
            }
        }
        return localStorage.getItem("pcwerk-build");
    },
    scrollToElement: function (elementId) {
        const element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        const modalScroller = element.closest(".configurator-modal-scroll");
        if (modalScroller) {
            const top = modalScroller.scrollTop
                + element.getBoundingClientRect().top
                - modalScroller.getBoundingClientRect().top
                - 12;
            modalScroller.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
            return;
        }

        const headerHeight = document.querySelector(".site-header")?.getBoundingClientRect().height ?? 0;
        const top = window.scrollY + element.getBoundingClientRect().top - headerHeight - 12;
        window.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
    },
    scrollToCategory: function (elementId) {
        window.pcBuilder.scrollToElement(elementId);
    }
};

window.pcLanguage = {
    get: function () {
        const url = new URL(window.location.href);
        if (url.searchParams.has("lang")) {
            url.searchParams.delete("lang");
            window.history.replaceState(window.history.state, "", url.toString());
        }

        try {
            const language = localStorage.getItem("pcwerk-language");
            if (language) {
                document.documentElement.lang = language.toLowerCase();
            }
            return language;
        } catch {
            return null;
        }
    },
    set: function (language) {
        document.documentElement.lang = language.toLowerCase();
        try {
            localStorage.setItem("pcwerk-language", language);
        } catch {
            // The current selection still works when browser storage is unavailable.
        }
    }
};

window.pcCart = {
    storageKey: "pcwerk-cart-v1",
    get: function () {
        try {
            return localStorage.getItem(window.pcCart.storageKey);
        } catch {
            return null;
        }
    },
    set: function (value) {
        try {
            localStorage.setItem(window.pcCart.storageKey, value);
        } catch {
            // The cart remains available for the current visit when storage is blocked.
        }
    },
    remove: function () {
        try {
            localStorage.removeItem(window.pcCart.storageKey);
        } catch {
            // Nothing else to do when storage is blocked.
        }
    }
};

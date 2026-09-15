(() => {
    const rotationDelay = 5000;
    const reduceMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");

    function readImages(element) {
        try {
            const images = JSON.parse(element.dataset.heroMedia ?? "[]");
            return Array.isArray(images)
                ? images.filter(image => image && typeof image.src === "string" && typeof image.alt === "string")
                : [];
        } catch {
            return [];
        }
    }

    function setDimensions(element, image) {
        if (image.width) element.width = image.width;
        if (image.height) element.height = image.height;
    }

    function initHeroMedia(element) {
        const images = readImages(element);
        if (images.length < 2 || reduceMotionQuery.matches) return;

        const slots = [element.querySelector(".hero-media-image"), document.createElement("img")];
        if (!slots[0]) return;

        slots[1].className = "commerce-hero-image hero-media-image";
        slots[1].alt = "";
        slots[1].setAttribute("aria-hidden", "true");
        element.append(slots[1]);

        let activeSlot = 0;
        let activeImage = 0;
        let nextImage = 1;
        let pending = null;
        let timeoutId = 0;
        let stopped = false;

        function prepareNext() {
            const image = new Image();
            const pendingImage = { image, index: nextImage, ready: false, failed: false, advance: false };
            pending = pendingImage;
            image.decoding = "async";
            image.fetchPriority = "low";
            image.onload = () => {
                if (pending !== pendingImage) return;
                pendingImage.ready = true;
                if (pendingImage.advance) showNext();
            };
            image.onerror = () => {
                if (pending !== pendingImage) return;
                pendingImage.failed = true;
                if (pendingImage.advance) skipNext();
            };
            image.src = images[pendingImage.index].src;
        }

        function schedule() {
            window.clearTimeout(timeoutId);
            timeoutId = window.setTimeout(() => {
                if (!pending) return;
                if (pending.ready) {
                    showNext();
                } else if (pending.failed) {
                    skipNext();
                } else {
                    pending.advance = true;
                }
            }, rotationDelay);
        }

        function skipNext() {
            nextImage = (nextImage + 1) % images.length;
            prepareNext();
            schedule();
        }

        function showNext() {
            if (stopped || !pending?.ready) return;

            const image = images[pending.index];
            const incomingSlot = 1 - activeSlot;
            const outgoingSlot = activeSlot;
            const incoming = slots[incomingSlot];
            const outgoing = slots[outgoingSlot];

            incoming.src = image.src;
            incoming.alt = image.alt;
            setDimensions(incoming, image);
            incoming.removeAttribute("aria-hidden");
            incoming.classList.add("is-active");

            outgoing.classList.remove("is-active");
            outgoing.alt = "";
            outgoing.setAttribute("aria-hidden", "true");

            activeSlot = incomingSlot;
            activeImage = pending.index;
            nextImage = (activeImage + 1) % images.length;
            pending = null;
            prepareNext();
            schedule();
        }

        function stop() {
            stopped = true;
            window.clearTimeout(timeoutId);
        }

        function handleVisibilityChange() {
            if (document.hidden) {
                window.clearTimeout(timeoutId);
            } else if (!stopped) {
                schedule();
            }
        }

        document.addEventListener("visibilitychange", handleVisibilityChange);
        reduceMotionQuery.addEventListener("change", event => {
            if (event.matches) stop();
        });

        prepareNext();
        schedule();
    }

    document.querySelectorAll("[data-hero-media]").forEach(initHeroMedia);
})();

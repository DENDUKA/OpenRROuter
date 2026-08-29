(function () {
    "use strict";

    var speedPxPerSecond = 20;
    var dragThresholdPx = 6;
    var maximumSupplementalCards = 100;

    function initialize(viewport) {
        if (!viewport || viewport.dataset.flagshipInitialized === "true") {
            return;
        }

        var track = viewport.querySelector(":scope > [data-flagship-track]");
        var mainCycle = track && track.querySelector(":scope > [data-flagship-cycle]:not([data-flagship-clone])");
        var originals = mainCycle ? Array.prototype.slice.call(mainCycle.children) : [];
        if (!track || !mainCycle || originals.length === 0) {
            return;
        }

        viewport.dataset.flagshipInitialized = "true";

        var cycleWidth = 0;
        var frameId = 0;
        var previousTime = 0;
        var hovered = false;
        var pointerActive = false;
        var dragged = false;
        var suppressNextClick = false;
        var activePointerId = null;
        var startX = 0;
        var startScrollLeft = 0;
        var reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

        function positiveModulo(value, divisor) {
            return ((value % divisor) + divisor) % divisor;
        }

        function makeNonSemantic(element) {
            element.dataset.flagshipClone = "";
            element.setAttribute("aria-hidden", "true");
            if (element.matches("a, button, input, select, textarea, [tabindex]")) {
                element.tabIndex = -1;
            }
            element.querySelectorAll("a, button, input, select, textarea, [tabindex]")
                .forEach(function (focusable) { focusable.tabIndex = -1; });
        }

        function removeClones() {
            track.querySelectorAll(":scope > [data-flagship-clone]")
                .forEach(function (clone) { clone.remove(); });
            mainCycle.querySelectorAll(":scope > [data-flagship-clone]")
                .forEach(function (clone) { clone.remove(); });
        }

        function fillCycle() {
            var targetWidth = viewport.clientWidth + 330;
            var supplementalCount = 0;

            while (mainCycle.scrollWidth < targetWidth && supplementalCount < maximumSupplementalCards) {
                var widthBeforeRound = mainCycle.scrollWidth;
                originals.forEach(function (original) {
                    if (supplementalCount >= maximumSupplementalCards) {
                        return;
                    }

                    var supplemental = original.cloneNode(true);
                    makeNonSemantic(supplemental);
                    mainCycle.appendChild(supplemental);
                    supplementalCount += 1;
                });

                if (mainCycle.scrollWidth <= widthBeforeRound) {
                    break;
                }
            }
        }

        function rebuild() {
            var preservedOffset = cycleWidth > 0
                ? positiveModulo(viewport.scrollLeft - cycleWidth, cycleWidth)
                : 0;

            removeClones();
            fillCycle();

            var before = mainCycle.cloneNode(true);
            var after = mainCycle.cloneNode(true);
            makeNonSemantic(before);
            makeNonSemantic(after);
            track.replaceChildren(before, mainCycle, after);

            var trackGap = Number.parseFloat(window.getComputedStyle(track).columnGap) || 0;
            cycleWidth = mainCycle.getBoundingClientRect().width + trackGap;
            if (cycleWidth > 0) {
                viewport.scrollLeft = cycleWidth + positiveModulo(preservedOffset, cycleWidth);
            }
        }

        function normalize() {
            if (cycleWidth <= 0) {
                return;
            }

            if (viewport.scrollLeft < cycleWidth || viewport.scrollLeft >= cycleWidth * 2) {
                viewport.scrollLeft = cycleWidth + positiveModulo(viewport.scrollLeft - cycleWidth, cycleWidth);
            }
        }

        function tick(timestamp) {
            if (previousTime === 0) {
                previousTime = timestamp;
            }

            var elapsed = Math.min(timestamp - previousTime, 50);
            previousTime = timestamp;
            if (!hovered && !pointerActive && !document.hidden) {
                viewport.scrollLeft += speedPxPerSecond * elapsed / 1000;
                normalize();
            }

            frameId = window.requestAnimationFrame(tick);
        }

        function startAnimation() {
            if (frameId !== 0 || reducedMotion.matches || document.hidden) {
                return;
            }

            previousTime = 0;
            frameId = window.requestAnimationFrame(tick);
        }

        function stopAnimation() {
            if (frameId === 0) {
                return;
            }

            window.cancelAnimationFrame(frameId);
            frameId = 0;
            previousTime = 0;
        }

        viewport.addEventListener("pointerenter", function (event) {
            if (event.pointerType === "mouse") {
                hovered = true;
            }
        });
        viewport.addEventListener("pointerleave", function (event) {
            if (event.pointerType === "mouse") {
                hovered = false;
            }
        });
        viewport.addEventListener("pointerdown", function (event) {
            if (pointerActive || !event.isPrimary || (event.pointerType === "mouse" && event.button !== 0)) {
                return;
            }

            pointerActive = true;
            activePointerId = event.pointerId;
            dragged = false;
            suppressNextClick = false;
            startX = event.clientX;
            startScrollLeft = viewport.scrollLeft;
        });
        viewport.addEventListener("pointermove", function (event) {
            if (!pointerActive || event.pointerId !== activePointerId) {
                return;
            }

            var delta = event.clientX - startX;
            if (!dragged && Math.abs(delta) > dragThresholdPx) {
                dragged = true;
                viewport.classList.add("is-dragging");
                if (!viewport.hasPointerCapture(event.pointerId)) {
                    viewport.setPointerCapture(event.pointerId);
                }
            }
            if (!dragged) {
                return;
            }

            event.preventDefault();
            viewport.scrollLeft = startScrollLeft - delta;
            normalize();
        });

        function finishPointer(event) {
            if (!pointerActive || event.pointerId !== activePointerId) {
                return;
            }

            pointerActive = false;
            activePointerId = null;
            suppressNextClick = dragged;
            dragged = false;
            viewport.classList.remove("is-dragging");
            if (viewport.hasPointerCapture(event.pointerId)) {
                viewport.releasePointerCapture(event.pointerId);
            }
        }

        window.addEventListener("pointerup", finishPointer);
        window.addEventListener("pointercancel", function (event) {
            dragged = false;
            finishPointer(event);
            suppressNextClick = false;
        });
        viewport.addEventListener("click", function (event) {
            if (!suppressNextClick) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            suppressNextClick = false;
        }, true);
        viewport.addEventListener("dragstart", function (event) { event.preventDefault(); });
        viewport.addEventListener("scroll", normalize, { passive: true });

        var resizeObserver = typeof ResizeObserver === "undefined"
            ? null
            : new ResizeObserver(rebuild);
        if (resizeObserver) {
            resizeObserver.observe(viewport);
        } else {
            window.addEventListener("resize", rebuild);
        }

        reducedMotion.addEventListener("change", function () {
            if (reducedMotion.matches) {
                stopAnimation();
            } else {
                startAnimation();
            }
        });
        function resetInteraction() {
            if (activePointerId !== null && viewport.hasPointerCapture(activePointerId)) {
                viewport.releasePointerCapture(activePointerId);
            }
            pointerActive = false;
            activePointerId = null;
            dragged = false;
            suppressNextClick = false;
            viewport.classList.remove("is-dragging");
        }

        document.addEventListener("visibilitychange", function () {
            if (document.hidden) {
                resetInteraction();
                stopAnimation();
            } else {
                startAnimation();
            }
        });
        window.addEventListener("pagehide", function () {
            resetInteraction();
            hovered = false;
            stopAnimation();
        });
        window.addEventListener("pageshow", function () {
            hovered = viewport.matches(":hover");
            startAnimation();
        });

        rebuild();
        startAnimation();
    }

    window.FlagshipCarousel = { initialize: initialize };
    document.querySelectorAll("[data-flagship-carousel]").forEach(initialize);
})();

(function () {
    "use strict";

    var speedPxPerSecond = 20;
    var dragThresholdPx = 6;
    var maximumSupplementalCards = 100;
    var resumeDelayMs = 800;
    var inertiaDecayPerFrame = 0.92;
    var minimumInertiaVelocityPxPerMs = 0.01;
    var maximumInertiaVelocityPxPerMs = 2;
    var maximumVelocitySampleAgeMs = 80;

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
        originals.forEach(function (original, index) {
            original.dataset.flagshipOriginalIndex = String(index);
        });

        var cycleWidth = 0;
        var frameId = 0;
        var previousTime = 0;
        var hoveredCycle = false;
        var pointerActive = false;
        var dragged = false;
        var suppressNextClick = false;
        var activePointerId = null;
        var startX = 0;
        var startScrollLeft = 0;
        var scrollRemainder = 0;
        var inertiaVelocityPxPerMs = 0;
        var lastPointerX = 0;
        var lastPointerTime = 0;
        var resumeAutoplayAt = 0;
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

        function cycleFromNode(node) {
            if (!(node instanceof Element)) {
                return null;
            }

            var cycle = node.closest("[data-flagship-cycle]");
            return cycle && track.contains(cycle) ? cycle : null;
        }

        function originalForClone(node) {
            if (!(node instanceof Element)) {
                return null;
            }

            var card = node.closest(".flagship-card");
            if (!card || !track.contains(card) || !card.closest("[data-flagship-clone]")) {
                return null;
            }

            return originals[Number(card.dataset.flagshipOriginalIndex)] || null;
        }

        function redirectClonePointerFocus(event) {
            var original = originalForClone(event.target);
            if (original) {
                // tabindex=-1 still permits pointer focus. Cancel that default, not native link activation.
                event.preventDefault();
                original.focus({ preventScroll: true });
            }
        }

        function cancelInertia() {
            inertiaVelocityPxPerMs = 0;
            scrollRemainder = 0;
        }

        function clampInertiaVelocity(velocity) {
            return Math.max(
                -maximumInertiaVelocityPxPerMs,
                Math.min(maximumInertiaVelocityPxPerMs, velocity));
        }

        function synchronizeCycleHover() {
            var wasHoveredCycle = hoveredCycle;
            hoveredCycle = track.querySelector("[data-flagship-cycle]:hover") !== null;
            if (hoveredCycle) {
                cancelInertia();
                resumeAutoplayAt = Number.POSITIVE_INFINITY;
            } else if (wasHoveredCycle || !Number.isFinite(resumeAutoplayAt)) {
                resumeAutoplayAt = performance.now() + resumeDelayMs;
            }
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
            scrollRemainder = 0;
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

            synchronizeCycleHover();
        }

        function normalize() {
            if (cycleWidth <= 0) {
                return;
            }

            if (viewport.scrollLeft < cycleWidth || viewport.scrollLeft >= cycleWidth * 2) {
                viewport.scrollLeft = cycleWidth + positiveModulo(viewport.scrollLeft - cycleWidth, cycleWidth);
            }
        }

        function moveBy(delta) {
            if (cycleWidth <= 0) {
                return;
            }

            var target = viewport.scrollLeft + scrollRemainder + delta;
            viewport.scrollLeft = target;
            scrollRemainder = target - viewport.scrollLeft;
            normalize();
        }

        function tick(timestamp) {
            if (previousTime === 0) {
                previousTime = timestamp;
            }

            var elapsed = Math.min(timestamp - previousTime, 50);
            previousTime = timestamp;
            if (!hoveredCycle && !pointerActive && !document.hidden && !reducedMotion.matches) {
                if (Math.abs(inertiaVelocityPxPerMs) >= minimumInertiaVelocityPxPerMs) {
                    moveBy(inertiaVelocityPxPerMs * elapsed);
                    inertiaVelocityPxPerMs *= Math.pow(inertiaDecayPerFrame, elapsed / (1000 / 60));
                    if (Math.abs(inertiaVelocityPxPerMs) < minimumInertiaVelocityPxPerMs) {
                        cancelInertia();
                        resumeAutoplayAt = timestamp + resumeDelayMs;
                    }
                } else if (timestamp >= resumeAutoplayAt) {
                    moveBy(speedPxPerSecond * elapsed / 1000);
                }
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

        track.addEventListener("pointerover", function (event) {
            if (event.pointerType !== "mouse") {
                return;
            }

            var destinationCycle = cycleFromNode(event.target);
            var sourceCycle = cycleFromNode(event.relatedTarget);
            if (!destinationCycle || destinationCycle === sourceCycle) {
                return;
            }

            hoveredCycle = true;
            cancelInertia();
            resumeAutoplayAt = Number.POSITIVE_INFINITY;
        });
        track.addEventListener("pointerout", function (event) {
            if (event.pointerType !== "mouse") {
                return;
            }

            var sourceCycle = cycleFromNode(event.target);
            var destinationCycle = cycleFromNode(event.relatedTarget);
            if (!sourceCycle || sourceCycle === destinationCycle) {
                return;
            }
            if (destinationCycle) {
                hoveredCycle = true;
                return;
            }

            hoveredCycle = false;
            resumeAutoplayAt = performance.now() + resumeDelayMs;
        });
        viewport.addEventListener("pointerdown", redirectClonePointerFocus, true);
        viewport.addEventListener("mousedown", redirectClonePointerFocus, true);
        viewport.addEventListener("focusin", function (event) {
            // Also guard programmatic focus and browser focus restoration onto either kind of clone.
            var original = originalForClone(event.target);
            if (original) {
                original.focus({ preventScroll: true });
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
            lastPointerX = event.clientX;
            lastPointerTime = performance.now();
            cancelInertia();
            resumeAutoplayAt = Number.POSITIVE_INFINITY;
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

            var now = performance.now();
            var sampleMs = Math.max(now - lastPointerTime, 1);
            var sampleVelocity = clampInertiaVelocity(-(event.clientX - lastPointerX) / sampleMs);
            inertiaVelocityPxPerMs = clampInertiaVelocity(
                inertiaVelocityPxPerMs * 0.35 + sampleVelocity * 0.65);
            lastPointerX = event.clientX;
            lastPointerTime = now;
        });

        function finishPointer(event, cancelled) {
            if (!pointerActive || event.pointerId !== activePointerId) {
                return;
            }

            var completedDrag = dragged && !cancelled;
            pointerActive = false;
            activePointerId = null;
            suppressNextClick = completedDrag;
            dragged = false;
            viewport.classList.remove("is-dragging");
            if (viewport.hasPointerCapture(event.pointerId)) {
                viewport.releasePointerCapture(event.pointerId);
            }

            var pointerReleasedAt = performance.now();
            if (hoveredCycle) {
                cancelInertia();
                resumeAutoplayAt = Number.POSITIVE_INFINITY;
            } else if (reducedMotion.matches || !completedDrag) {
                cancelInertia();
                resumeAutoplayAt = pointerReleasedAt + resumeDelayMs;
            } else if (pointerReleasedAt - lastPointerTime > maximumVelocitySampleAgeMs) {
                cancelInertia();
                resumeAutoplayAt = pointerReleasedAt + resumeDelayMs;
            } else if (Math.abs(inertiaVelocityPxPerMs) < minimumInertiaVelocityPxPerMs) {
                cancelInertia();
                resumeAutoplayAt = pointerReleasedAt + resumeDelayMs;
            } else {
                resumeAutoplayAt = Number.POSITIVE_INFINITY;
            }
        }

        window.addEventListener("pointerup", function (event) { finishPointer(event, false); });
        window.addEventListener("pointercancel", function (event) {
            finishPointer(event, true);
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
        viewport.addEventListener("wheel", function () {
            cancelInertia();
            resumeAutoplayAt = performance.now() + resumeDelayMs;
        }, { passive: true });

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
                cancelInertia();
                stopAnimation();
            } else {
                resumeAutoplayAt = performance.now() + resumeDelayMs;
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
            cancelInertia();
            resumeAutoplayAt = Number.POSITIVE_INFINITY;
            viewport.classList.remove("is-dragging");
        }

        document.addEventListener("visibilitychange", function () {
            if (document.hidden) {
                resetInteraction();
                stopAnimation();
            } else {
                synchronizeCycleHover();
                startAnimation();
            }
        });
        window.addEventListener("pagehide", function () {
            resetInteraction();
            hoveredCycle = false;
            stopAnimation();
        });
        window.addEventListener("pageshow", function () {
            synchronizeCycleHover();
            startAnimation();
        });

        rebuild();
        startAnimation();
    }

    window.FlagshipCarousel = { initialize: initialize };
    document.querySelectorAll("[data-flagship-carousel]").forEach(initialize);
})();

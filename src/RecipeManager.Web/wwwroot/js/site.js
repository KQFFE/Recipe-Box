// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const layoutPreview = document.querySelector(".layout-editor-preview");

if (layoutPreview) {
  let draggedSectionKey;
  let touchDraggedSectionKey;
  let touchPointerId;
  let touchPointerX;
  let touchPointerY;
  let touchScrollFrame;
  let activeDropTarget;

  const previewSections = () =>
    Array.from(layoutPreview.querySelectorAll("[data-layout-section]"));

  const setDropTarget = (section) => {
    if (activeDropTarget === section) {
      return;
    }

    activeDropTarget?.classList.remove("layout-drop-target");
    activeDropTarget = section;
    activeDropTarget?.classList.add("layout-drop-target");
  };

  const clearDropTarget = () => setDropTarget(undefined);

  const updateTouchDropTarget = () => {
    const target = document.elementFromPoint(touchPointerX, touchPointerY)
      ?.closest("[data-layout-section]");
    setDropTarget(target?.dataset.layoutSection === touchDraggedSectionKey ? undefined : target);
  };

  const scrollWhileDragging = () => {
    if (touchPointerId === undefined) {
      return;
    }

    const edgeSize = Math.min(96, window.innerHeight * 0.16);
    const distanceFromTop = touchPointerY;
    const distanceFromBottom = window.innerHeight - touchPointerY;
    if (distanceFromTop < edgeSize) {
      const intensity = 1 - Math.max(0, distanceFromTop) / edgeSize;
      window.scrollBy(0, -Math.max(1, Math.ceil(intensity * 18)));
    } else if (distanceFromBottom < edgeSize) {
      const intensity = 1 - Math.max(0, distanceFromBottom) / edgeSize;
      window.scrollBy(0, Math.max(1, Math.ceil(intensity * 18)));
    }

    updateTouchDropTarget();
    touchScrollFrame = window.requestAnimationFrame(scrollWhileDragging);
  };

  const moveSection = (key, targetKey) => {
    const sourceSection = layoutPreview.querySelector(`[data-layout-section="${key}"]`);
    const targetSection = layoutPreview.querySelector(`[data-layout-section="${targetKey}"]`);

    if (
      !sourceSection ||
      !targetSection ||
      sourceSection === targetSection ||
      sourceSection.parentNode !== targetSection.parentNode
    ) {
      return;
    }

    const sourceMarker = document.createComment("section swap");
    sourceSection.parentNode.insertBefore(sourceMarker, sourceSection);
    targetSection.parentNode.insertBefore(sourceSection, targetSection);
    sourceMarker.parentNode.replaceChild(targetSection, sourceMarker);
    updateMoveButtons();
  };

  const updateMoveButtons = () => {
    const sections = previewSections();
    sections.forEach((section, index) => {
      section.querySelector('[data-layout-move="up"]').disabled = index === 0;
      section.querySelector('[data-layout-move="down"]').disabled = index === sections.length - 1;
    });
  };

  layoutPreview.addEventListener("click", (event) => {
    const button = event.target.closest("[data-layout-move]");
    if (!button) {
      return;
    }

    event.preventDefault();
    const section = button.closest("[data-layout-section]");
    const sections = previewSections();
    const index = sections.indexOf(section);
    const targetSection = sections[index + (button.dataset.layoutMove === "up" ? -1 : 1)];
    if (!section || !targetSection) {
      return;
    }

    moveSection(
      section.dataset.layoutSection,
      targetSection.dataset.layoutSection
    );
  });

  previewSections().forEach((section) => {
    section.draggable = true;
    section.setAttribute("aria-roledescription", "Draggable section");
    section.setAttribute(
      "aria-label",
      `${section.querySelector("h2")?.textContent || section.dataset.layoutSection}. Use the move buttons to change its position.`
    );
  });

  const finishTouchDrag = (event, shouldDrop) => {
    if (event.pointerId !== touchPointerId) {
      return;
    }

    if (shouldDrop && touchDraggedSectionKey) {
      const target = document.elementFromPoint(event.clientX, event.clientY)?.closest("[data-layout-section]");
      if (target && target.dataset.layoutSection !== touchDraggedSectionKey) {
        moveSection(touchDraggedSectionKey, target.dataset.layoutSection);
      }
    }

    clearDropTarget();
    previewSections().forEach((section) => section.classList.remove("layout-section-dragging"));
    if (touchScrollFrame !== undefined) {
      window.cancelAnimationFrame(touchScrollFrame);
      touchScrollFrame = undefined;
    }
    touchDraggedSectionKey = undefined;
    touchPointerId = undefined;
    window.removeEventListener("pointermove", onTouchPointerMove);
    window.removeEventListener("pointerup", onTouchPointerUp);
    window.removeEventListener("pointercancel", onTouchPointerCancel);
  };

  const onTouchPointerMove = (event) => {
    if (event.pointerId === touchPointerId) {
      event.preventDefault();
      touchPointerX = event.clientX;
      touchPointerY = event.clientY;
      updateTouchDropTarget();
    }
  };

  const onTouchPointerUp = (event) => finishTouchDrag(event, true);
  const onTouchPointerCancel = (event) => finishTouchDrag(event, false);

  layoutPreview.addEventListener("pointerdown", (event) => {
    const handle = event.target.closest("[data-layout-drag-handle]");
    if (!handle || event.pointerType === "mouse") {
      return;
    }

    event.preventDefault();
    touchDraggedSectionKey = handle.dataset.layoutDragHandle;
    touchPointerId = event.pointerId;
    touchPointerX = event.clientX;
    touchPointerY = event.clientY;
    layoutPreview
      .querySelector(`[data-layout-section="${touchDraggedSectionKey}"]`)
      .classList.add("layout-section-dragging");
    setDropTarget(undefined);
    touchScrollFrame = window.requestAnimationFrame(scrollWhileDragging);
    window.addEventListener("pointermove", onTouchPointerMove, { passive: false });
    window.addEventListener("pointerup", onTouchPointerUp);
    window.addEventListener("pointercancel", onTouchPointerCancel);
  });

  layoutPreview.addEventListener("dragstart", (event) => {
    const section = event.target.closest("[data-layout-section]");
    if (!section || !event.dataTransfer) {
      return;
    }

    draggedSectionKey = section.dataset.layoutSection;
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", draggedSectionKey);
    section.classList.add("layout-section-dragging");
  });

  layoutPreview.addEventListener("dragover", (event) => {
    const target = event.target.closest("[data-layout-section]");
    if (!target || target.dataset.layoutSection === draggedSectionKey) {
      setDropTarget(undefined);
      return;
    }

    event.preventDefault();
    setDropTarget(target);
    event.dataTransfer.dropEffect = "move";
  });

  layoutPreview.addEventListener("dragleave", (event) => {
    const section = event.target.closest("[data-layout-section]");
    if (section && !section.contains(event.relatedTarget)) {
      setDropTarget(undefined);
    }
  });

  layoutPreview.addEventListener("drop", (event) => {
    const target = event.target.closest("[data-layout-section]");
    const key = event.dataTransfer?.getData("text/plain") || draggedSectionKey;
    if (!target || !key || target.dataset.layoutSection === key) {
      return;
    }

    event.preventDefault();
    moveSection(key, target.dataset.layoutSection);
    clearDropTarget();
  });

  layoutPreview.addEventListener("dragend", () => {
    clearDropTarget();
    previewSections().forEach((section) => section.classList.remove("layout-section-dragging"));
    draggedSectionKey = undefined;
  });

  document.querySelectorAll("[data-layout-width]").forEach((checkbox) => {
    checkbox.addEventListener("change", () => {
      const section = layoutPreview.querySelector(`[data-layout-section="${checkbox.dataset.layoutWidth}"]`);
      section?.classList.toggle("recipe-layout-panel-full", checkbox.checked);
    });
  });
}

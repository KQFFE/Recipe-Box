// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const layoutPreview = document.querySelector(".layout-editor-preview");

if (layoutPreview) {
  const sectionOrder = document.querySelector("#layout-section-order");
  let draggedSectionKey;

  const previewSections = () =>
    Array.from(layoutPreview.querySelectorAll("[data-layout-section]"));

  const moveSection = (key, targetKey, insertAfter) => {
    const sourceItem = sectionOrder.querySelector(`[data-layout-editor-item="${key}"]`);
    const targetItem = sectionOrder.querySelector(`[data-layout-editor-item="${targetKey}"]`);
    const sourceSection = layoutPreview.querySelector(`[data-layout-section="${key}"]`);
    const targetSection = layoutPreview.querySelector(`[data-layout-section="${targetKey}"]`);

    if (
      !sourceItem ||
      !targetItem ||
      !sourceSection ||
      !targetSection ||
      sourceItem === targetItem ||
      sourceSection.parentNode !== targetSection.parentNode
    ) {
      return;
    }

    sectionOrder.insertBefore(sourceItem, insertAfter ? targetItem.nextSibling : targetItem);
    sourceSection.parentNode.insertBefore(
      sourceSection,
      insertAfter ? targetSection.nextSibling : targetSection
    );
    updateMoveButtons();
  };

  const updateMoveButtons = () => {
    const items = Array.from(sectionOrder.querySelectorAll("[data-layout-editor-item]"));
    items.forEach((item, index) => {
      item.querySelector('[data-layout-move="up"]').disabled = index === 0;
      item.querySelector('[data-layout-move="down"]').disabled = index === items.length - 1;
    });
  };

  sectionOrder.addEventListener("click", (event) => {
    const button = event.target.closest("[data-layout-move]");
    if (!button) {
      return;
    }

    event.preventDefault();
    const items = Array.from(sectionOrder.querySelectorAll("[data-layout-editor-item]"));
    const item = button.closest("[data-layout-editor-item]");
    const index = items.indexOf(item);
    const targetIndex = index + (button.dataset.layoutMove === "up" ? -1 : 1);
    const targetItem = items[targetIndex];
    if (!item || !targetItem) {
      return;
    }

    moveSection(
      item.dataset.layoutEditorItem,
      targetItem.dataset.layoutEditorItem,
      button.dataset.layoutMove === "down"
    );
  });

  previewSections().forEach((section) => {
    section.draggable = true;
    section.tabIndex = 0;
    section.setAttribute("aria-roledescription", "Draggable section");
    section.setAttribute(
      "aria-label",
      `Drag ${section.querySelector("h2")?.textContent || section.dataset.layoutSection} to change its position. Use the move buttons for keyboard-accessible reordering.`
    );
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
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
  });

  layoutPreview.addEventListener("drop", (event) => {
    const target = event.target.closest("[data-layout-section]");
    const key = event.dataTransfer?.getData("text/plain") || draggedSectionKey;
    if (!target || !key || target.dataset.layoutSection === key) {
      return;
    }

    event.preventDefault();
    const bounds = target.getBoundingClientRect();
    moveSection(key, target.dataset.layoutSection, event.clientY >= bounds.top + bounds.height / 2);
  });

  layoutPreview.addEventListener("dragend", () => {
    previewSections().forEach((section) => section.classList.remove("layout-section-dragging"));
    draggedSectionKey = undefined;
  });

  document.querySelectorAll("[data-layout-width]").forEach((checkbox) => {
    checkbox.addEventListener("change", () => {
      const section = layoutPreview.querySelector(
        `[data-layout-section="${checkbox.dataset.layoutWidth}"]`
      );
      section?.classList.toggle("recipe-layout-panel-full", checkbox.checked);
    });
  });
}

// boardDrag.js — HTML5 drag & drop for the Kanban board.
//
// Why this is not done with Blazor @ondrag* events: moving a card across lanes
// needs three things Blazor cannot supply reliably —
//   1. dataTransfer.setData on dragstart (some browsers refuse the drop otherwise),
//   2. preventDefault() on dragover, which is what actually enables dropping,
//   3. a custom drag image plus correct dropEffect.
// Blazor's DragEventArgs only exposes a limited view of dataTransfer, so the
// browser half lives here and hands the result back to .NET via a DotNetObjectReference.
//
// Lane and card elements are located by data attributes, so the module keeps
// working when Blazor re-renders the board.

let dotNetRef = null;
let handlers = null;

const BOARD_SELECTOR = "[data-board]";
const LANE_SELECTOR = "[data-lane-status]";
const CARD_SELECTOR = "[data-task-id]";

function lanes() {
    return Array.from(document.querySelectorAll(LANE_SELECTOR));
}

function clearDropTargets() {
    for (const lane of lanes()) {
        lane.classList.remove("lane--drop");
    }
}

function onDragStart(e) {
    const card = e.target.closest?.(CARD_SELECTOR);
    if (!card) {
        return;
    }

    const taskId = card.dataset.taskId;
    e.dataTransfer.effectAllowed = "move";
    // Firefox will not start a drag unless some data is set.
    e.dataTransfer.setData("text/plain", taskId);
    e.dataTransfer.setData("application/x-kanban-task", taskId);

    // Dim the original so the lane still reads correctly while dragging.
    card.classList.add("card--dragging");
    document.body.classList.add("is-dragging-card");
}

function onDragEnd(e) {
    const card = e.target.closest?.(CARD_SELECTOR);
    card?.classList.remove("card--dragging");
    document.body.classList.remove("is-dragging-card");
    clearDropTargets();
}

function onDragOver(e) {
    // Without preventDefault the drop event never fires — this is the step that
    // makes an element a valid drop target.
    const lane = e.target.closest?.(LANE_SELECTOR);
    if (!lane) {
        return;
    }

    e.preventDefault();
    e.dataTransfer.dropEffect = "move";

    for (const other of lanes()) {
        other.classList.toggle("lane--drop", other === lane);
    }
}

function onDragLeave(e) {
    const lane = e.target.closest?.(LANE_SELECTOR);
    if (!lane) {
        return;
    }

    // Ignore the leave events fired while moving between a lane's own children.
    if (lane.contains(e.relatedTarget)) {
        return;
    }

    lane.classList.remove("lane--drop");
}

function onDrop(e) {
    const lane = e.target.closest?.(LANE_SELECTOR);
    if (!lane) {
        return;
    }

    e.preventDefault();
    clearDropTargets();
    document.body.classList.remove("is-dragging-card");

    const taskId =
        e.dataTransfer.getData("application/x-kanban-task") ||
        e.dataTransfer.getData("text/plain");
    const status = parseInt(lane.dataset.laneStatus, 10);

    if (!taskId || Number.isNaN(status)) {
        return;
    }

    dotNetRef?.invokeMethodAsync("OnCardDropped", taskId, status);
}

/// <summary>Idempotently attaches the board drag handlers.</summary>
export function start(boardId) {
    const board = document.querySelector(BOARD_SELECTOR);
    if (!board || board.dataset.dragReady === "true") {
        return;
    }

    board.addEventListener("dragstart", onDragStart);
    board.addEventListener("dragend", onDragEnd);
    board.addEventListener("dragover", onDragOver);
    board.addEventListener("dragleave", onDragLeave);
    board.addEventListener("drop", onDrop);

    board.dataset.dragReady = "true";
}

/// <summary>Registers the .NET callback that receives successful drops.</summary>
export function attach(boardId, ref) {
    dotNetRef = ref;
    start(boardId);
}

export function stop(boardId) {
    const board = document.querySelector(BOARD_SELECTOR);
    if (!board || board.dataset.dragReady !== "true") {
        return;
    }

    board.removeEventListener("dragstart", onDragStart);
    board.removeEventListener("dragend", onDragEnd);
    board.removeEventListener("dragover", onDragOver);
    board.removeEventListener("dragleave", onDragLeave);
    board.removeEventListener("drop", onDrop);

    delete board.dataset.dragReady;
    dotNetRef = null;
}

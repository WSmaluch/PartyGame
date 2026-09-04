import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DrawingCanvas } from './DrawingCanvas';

const labels = { canvas: 'Drawing area', undo: 'Undo stroke', redo: 'Redo stroke', clear: 'Clear drawing', clearConfirm: 'Clear this drawing?', cancel: 'Cancel', eraser: 'Eraser', colors: { black: 'Black', white: 'White', red: 'Red', blue: 'Blue', green: 'Green', yellow: 'Yellow', orange: 'Orange', purple: 'Purple', pink: 'Pink', brown: 'Brown' }, widths: { 4: 'Thin line', 10: 'Medium line', 22: 'Thick line' } };

function canvasContext(): CanvasRenderingContext2D {
  return { fillStyle: '', strokeStyle: '', lineWidth: 0, lineCap: '', lineJoin: '', globalCompositeOperation: 'source-over', fillRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn(), save: vi.fn(), restore: vi.fn(), drawImage: vi.fn() } as unknown as CanvasRenderingContext2D;
}

describe('DrawingCanvas', () => {
  afterEach(() => vi.restoreAllMocks());

  it('normalizes pointer coordinates and supports undo, redo, plus confirmed clear', () => {
    const context = canvasContext(); vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context);
    const onCanvas = vi.fn(); const onInkChange = vi.fn(); render(<DrawingCanvas disabled={false} onCanvas={onCanvas} onInkChange={onInkChange} labels={labels} />);
    const canvas = screen.getByRole('img', { name: 'Drawing area' }); vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, width: 200, height: 100, top: 0, right: 200, bottom: 100, left: 0, toJSON: () => ({}) }); Object.defineProperty(canvas, 'setPointerCapture', { configurable: true, value: vi.fn() });
    fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 0, clientY: 0 }); fireEvent.pointerMove(canvas, { pointerId: 1, clientX: 100, clientY: 25 }); fireEvent.pointerUp(canvas, { pointerId: 1, clientX: 100, clientY: 25 });
    expect(onInkChange).toHaveBeenLastCalledWith(true); expect(context.lineTo).toHaveBeenCalledWith(512, 256);
    fireEvent.click(screen.getByRole('button', { name: 'Undo stroke' })); expect(onInkChange).toHaveBeenLastCalledWith(false); expect(screen.getByRole('button', { name: 'Redo stroke' })).toBeEnabled(); fireEvent.click(screen.getByRole('button', { name: 'Redo stroke' })); expect(onInkChange).toHaveBeenLastCalledWith(true);
    fireEvent.click(screen.getByRole('button', { name: 'Clear drawing' })); expect(screen.getByRole('alertdialog', { name: 'Clear this drawing?' })).toBeInTheDocument(); fireEvent.click(screen.getAllByRole('button', { name: 'Clear drawing' })[1]);
    expect(onInkChange).toHaveBeenLastCalledWith(false); expect(onCanvas).toHaveBeenCalledWith(expect.any(HTMLCanvasElement));
  });

  it('matches the iOS palette, line widths, eraser, and redo controls', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext()); render(<DrawingCanvas disabled={false} onCanvas={vi.fn()} onInkChange={vi.fn()} labels={labels} />);
    expect(['Black', 'White', 'Red', 'Blue', 'Green', 'Yellow', 'Orange', 'Purple', 'Pink', 'Brown'].map((name) => screen.getByRole('button', { name }))).toHaveLength(10);
    expect(['Thin line', 'Medium line', 'Thick line'].map((name) => screen.getByRole('button', { name }))).toHaveLength(3); expect(screen.getByRole('button', { name: 'Eraser' })).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Redo stroke' })).toBeDisabled();
  });

  it('does not require queueMicrotask or pointer capture before mounting the drawing tools', () => {
    const originalQueueMicrotask = globalThis.queueMicrotask; Object.defineProperty(globalThis, 'queueMicrotask', { configurable: true, value: undefined });
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext()); render(<DrawingCanvas disabled={false} onCanvas={vi.fn()} onInkChange={vi.fn()} labels={labels} />);
    expect(screen.getByRole('img', { name: 'Drawing area' })).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Eraser' })).toBeInTheDocument();
    Object.defineProperty(globalThis, 'queueMicrotask', { configurable: true, value: originalQueueMicrotask });
  });
});

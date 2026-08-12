import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DrawingCanvas } from './DrawingCanvas';

const labels = { canvas: 'Drawing area', undo: 'Undo stroke', clear: 'Clear drawing', clearConfirm: 'Clear this drawing?', cancel: 'Cancel' };

describe('DrawingCanvas', () => {
  afterEach(() => vi.restoreAllMocks());

  it('normalizes pointer coordinates and supports undo plus confirmed clear', () => {
    const context = { fillStyle: '', strokeStyle: '', lineWidth: 0, lineCap: '', lineJoin: '', fillRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() } as unknown as CanvasRenderingContext2D;
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context);
    const onCanvas = vi.fn(); const onInkChange = vi.fn();
    render(<DrawingCanvas disabled={false} onCanvas={onCanvas} onInkChange={onInkChange} labels={labels} />);
    const canvas = screen.getByRole('img', { name: 'Drawing area' });
    vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, width: 200, height: 100, top: 0, right: 200, bottom: 100, left: 0, toJSON: () => ({}) });
    Object.defineProperty(canvas, 'setPointerCapture', { configurable: true, value: vi.fn() });
    fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 0, clientY: 0 }); fireEvent.pointerMove(canvas, { pointerId: 1, clientX: 100, clientY: 25 }); fireEvent.pointerUp(canvas, { pointerId: 1, clientX: 100, clientY: 25 });
    expect(onInkChange).toHaveBeenLastCalledWith(true); expect(context.lineTo).toHaveBeenCalledWith(512, 256);
    fireEvent.click(screen.getByRole('button', { name: 'Undo stroke' })); expect(onInkChange).toHaveBeenLastCalledWith(false);
    fireEvent.pointerDown(canvas, { pointerId: 1, clientX: 20, clientY: 20 }); fireEvent.pointerUp(canvas, { pointerId: 1, clientX: 20, clientY: 20 });
    fireEvent.click(screen.getByRole('button', { name: 'Clear drawing' })); expect(screen.getByRole('alertdialog', { name: 'Clear this drawing?' })).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Clear drawing' })[1]);
    expect(onInkChange).toHaveBeenLastCalledWith(false); expect(onCanvas).toHaveBeenCalledWith(expect.any(HTMLCanvasElement));
  });
});

import { useEffect, useRef, useState } from 'react';

type Point = { x: number; y: number };
type Tool = 'brush' | 'eraser';
type ColorName = keyof typeof colors;
type Stroke = { points: Point[]; color: ColorName; width: number; tool: Tool };

// This is the iOS DrawingColor/DrawingLineWidth palette: no Web-only tools or
// arbitrary widths are introduced. Strokes stay normalized across input types.
const colors = {
  black: '#000000',
  white: '#ffffff',
  red: '#ff3b30',
  blue: '#007aff',
  green: '#34c759',
  yellow: '#ffcc00',
  orange: '#ff9500',
  purple: '#af52de',
  pink: '#ff2d55',
  brown: '#a2845e',
} as const;
const widths = [4, 10, 22] as const;

export type DrawingLabels = {
  canvas: string;
  undo: string;
  redo: string;
  clear: string;
  clearConfirm: string;
  cancel: string;
  eraser: string;
  colorControl: string;
  widthControl: string;
  toolControl: string;
  moreControl: string;
  colors: Record<ColorName, string>;
  widths: Record<4 | 10 | 22, string>;
};

export function DrawingCanvas({
  disabled,
  onCanvas,
  onInkChange,
  labels,
  backgroundImage,
}: {
  disabled: boolean;
  onCanvas: (canvas: HTMLCanvasElement | undefined) => void;
  onInkChange: (hasInk: boolean) => void;
  labels: DrawingLabels;
  backgroundImage?: string;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const active = useRef<Stroke | undefined>(undefined);
  const [strokes, setStrokes] = useState<Stroke[]>([]);
  const [redoStack, setRedoStack] = useState<Stroke[]>([]);
  const [tool, setTool] = useState<Tool>('brush');
  const [color, setColor] = useState<ColorName>('black');
  const [width, setWidth] = useState<4 | 10 | 22>(10);
  const [confirmingClear, setConfirmingClear] = useState(false);
  const [openMenu, setOpenMenu] = useState<
    'colors' | 'widths' | 'tools' | 'more'
  >();
  const [background, setBackground] = useState<HTMLImageElement>();
  useEffect(() => {
    onCanvas(canvasRef.current ?? undefined);
    return () => onCanvas(undefined);
  }, [onCanvas]);
  useEffect(() => {
    let live = true;
    if (!backgroundImage) {
      defer(() => {
        if (live) setBackground(undefined);
      });
      return () => {
        live = false;
      };
    }
    const image = new Image();
    image.crossOrigin = 'anonymous';
    image.onload = () => {
      if (live) setBackground(image);
    };
    image.onerror = () => {
      if (live) setBackground(undefined);
    };
    image.src = backgroundImage;
    return () => {
      live = false;
    };
  }, [backgroundImage]);
  useEffect(() => {
    onInkChange(strokes.length > 0);
    redraw(canvasRef.current, strokes, active.current, background);
  }, [strokes, onInkChange, background]);
  function point(event: React.PointerEvent<HTMLCanvasElement>): Point {
    const rect = event.currentTarget.getBoundingClientRect();
    return {
      x: Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)),
      y: Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height)),
    };
  }
  function start(event: React.PointerEvent<HTMLCanvasElement>): void {
    if (disabled) return;
    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    active.current = { points: [point(event)], color, width, tool };
    renderActive();
  }
  function move(event: React.PointerEvent<HTMLCanvasElement>): void {
    if (!active.current || disabled) return;
    event.preventDefault();
    active.current.points.push(point(event));
    renderActive();
  }
  function finish(event: React.PointerEvent<HTMLCanvasElement>): void {
    if (!active.current || disabled) return;
    event.preventDefault();
    const stroke = active.current;
    active.current = undefined;
    setStrokes((current) => [...current, stroke]);
    setRedoStack([]);
  }
  function renderActive(): void {
    redraw(canvasRef.current, strokes, active.current, background);
  }
  function chooseColor(next: ColorName): void {
    setColor(next);
    setTool('brush');
    setOpenMenu(undefined);
  }
  function undo(): void {
    setStrokes((current) => {
      const stroke = current.at(-1);
      if (!stroke) return current;
      setRedoStack((redo) => [...redo, stroke]);
      return current.slice(0, -1);
    });
  }
  function redo(): void {
    setRedoStack((current) => {
      const stroke = current.at(-1);
      if (!stroke) return current;
      setStrokes((strokes) => [...strokes, stroke]);
      return current.slice(0, -1);
    });
  }
  function clear(): void {
    setStrokes((current) => {
      if (current.length)
        setRedoStack((redo) => [...redo, ...current.slice().reverse()]);
      return [];
    });
    setConfirmingClear(false);
  }
  function toggleMenu(menu: typeof openMenu): void {
    setOpenMenu((current) => (current === menu ? undefined : menu));
  }
  return (
    <section className="drawing-editor">
      <canvas
        ref={canvasRef}
        className="drawing-canvas"
        width="1024"
        height="1024"
        aria-label={labels.canvas}
        role="img"
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={finish}
        onPointerCancel={finish}
      />
      <div className="drawing-toolbar" aria-label={labels.canvas}>
        <button
          type="button"
          className="drawing-control drawing-color"
          aria-label={labels.colorControl}
          aria-expanded={openMenu === 'colors'}
          aria-controls="drawing-colors"
          onClick={() => toggleMenu('colors')}
        >
          <span style={{ background: colors[color] }} />
        </button>
        <button
          type="button"
          className="drawing-control drawing-width"
          aria-label={labels.widthControl}
          aria-expanded={openMenu === 'widths'}
          aria-controls="drawing-widths"
          onClick={() => toggleMenu('widths')}
        >
          <span style={{ width, height: width }} />
        </button>
        <button
          type="button"
          className={`drawing-control ${tool === 'eraser' ? 'is-selected' : ''}`}
          aria-label={labels.toolControl}
          aria-expanded={openMenu === 'tools'}
          aria-controls="drawing-tools"
          onClick={() => toggleMenu('tools')}
        >
          {tool === 'eraser' ? '⌫' : '✎'}
        </button>
        <button
          type="button"
          className="drawing-control"
          onClick={undo}
          disabled={disabled || !strokes.length}
        >
          {labels.undo}
        </button>
        <button
          type="button"
          className="drawing-control"
          onClick={redo}
          disabled={disabled || !redoStack.length}
        >
          {labels.redo}
        </button>
        <button
          type="button"
          className="drawing-control"
          aria-label={labels.moreControl}
          aria-expanded={openMenu === 'more'}
          aria-controls="drawing-more"
          onClick={() => toggleMenu('more')}
        >
          •••
        </button>
      </div>
      {openMenu === 'colors' && (
        <div
          id="drawing-colors"
          className="drawing-menu drawing-menu--colors"
          role="group"
          aria-label={labels.colorControl}
        >
          {(Object.keys(colors) as ColorName[]).map((name) => (
            <button
              key={name}
              className={`drawing-color ${color === name && tool === 'brush' ? 'is-selected' : ''}`}
              type="button"
              aria-label={labels.colors[name]}
              aria-pressed={color === name && tool === 'brush'}
              onClick={() => chooseColor(name)}
            >
              <span style={{ background: colors[name] }} />
            </button>
          ))}
        </div>
      )}
      {openMenu === 'widths' && (
        <div
          id="drawing-widths"
          className="drawing-menu"
          role="group"
          aria-label={labels.widthControl}
        >
          {widths.map((next) => (
            <button
              key={next}
              type="button"
              className={`drawing-width ${width === next && tool === 'brush' ? 'is-selected' : ''}`}
              aria-label={labels.widths[next]}
              aria-pressed={width === next && tool === 'brush'}
              onClick={() => {
                setWidth(next);
                setTool('brush');
                setOpenMenu(undefined);
              }}
            >
              <span style={{ width: next, height: next }} />
            </button>
          ))}
        </div>
      )}
      {openMenu === 'tools' && (
        <div
          id="drawing-tools"
          className="drawing-menu"
          role="group"
          aria-label={labels.toolControl}
        >
          <button
            type="button"
            className={tool === 'brush' ? 'is-selected' : ''}
            aria-pressed={tool === 'brush'}
            onClick={() => {
              setTool('brush');
              setOpenMenu(undefined);
            }}
          >
            {labels.toolControl}
          </button>
          <button
            type="button"
            className={tool === 'eraser' ? 'is-selected' : ''}
            aria-pressed={tool === 'eraser'}
            onClick={() => {
              setTool('eraser');
              setOpenMenu(undefined);
            }}
          >
            {labels.eraser}
          </button>
        </div>
      )}
      {openMenu === 'more' && (
        <div
          id="drawing-more"
          className="drawing-menu"
          role="group"
          aria-label={labels.moreControl}
        >
          <button
            type="button"
            onClick={() => {
              setConfirmingClear(true);
              setOpenMenu(undefined);
            }}
            disabled={disabled || !strokes.length}
          >
            {labels.clear}
          </button>
        </div>
      )}
      {confirmingClear && (
        <div
          className="drawing-clear-confirmation"
          role="alertdialog"
          aria-label={labels.clearConfirm}
        >
          <p>{labels.clearConfirm}</p>
          <button type="button" onClick={() => setConfirmingClear(false)}>
            {labels.cancel}
          </button>
          <button type="button" onClick={clear}>
            {labels.clear}
          </button>
        </div>
      )}
    </section>
  );
}

function defer(callback: () => void): void {
  if (typeof queueMicrotask === 'function') queueMicrotask(callback);
  else window.setTimeout(callback, 0);
}

function redraw(
  canvas: HTMLCanvasElement | null,
  strokes: Stroke[],
  activeStroke: Stroke | undefined,
  background: HTMLImageElement | undefined,
): void {
  const context = canvas?.getContext('2d');
  if (!canvas || !context) return;
  context.fillStyle = '#fff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  if (background) drawContain(context, background, canvas);
  // Erasing affects the local ink layer only. The accumulated Final Edit
  // background remains immutable and is composited below it for the export.
  const ink = canvas.ownerDocument.createElement('canvas');
  ink.width = canvas.width;
  ink.height = canvas.height;
  const inkContext = ink.getContext('2d');
  if (!inkContext) return;
  for (const stroke of strokes) draw(inkContext, stroke, canvas);
  if (activeStroke) draw(inkContext, activeStroke, canvas);
  context.drawImage(ink, 0, 0);
}

function drawContain(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement,
  canvas: HTMLCanvasElement,
): void {
  const scale = Math.min(
    canvas.width / image.naturalWidth,
    canvas.height / image.naturalHeight,
  );
  const width = image.naturalWidth * scale;
  const height = image.naturalHeight * scale;
  context.drawImage(
    image,
    (canvas.width - width) / 2,
    (canvas.height - height) / 2,
    width,
    height,
  );
}

function draw(
  context: CanvasRenderingContext2D,
  stroke: Stroke,
  canvas: HTMLCanvasElement,
): void {
  const first = stroke.points[0];
  if (!first) return;
  context.save();
  context.globalCompositeOperation =
    stroke.tool === 'eraser' ? 'destination-out' : 'source-over';
  context.strokeStyle = colors[stroke.color];
  context.lineWidth = stroke.width;
  context.lineCap = 'round';
  context.lineJoin = 'round';
  context.beginPath();
  context.moveTo(first.x * canvas.width, first.y * canvas.height);
  for (const point of stroke.points.slice(1))
    context.lineTo(point.x * canvas.width, point.y * canvas.height);
  if (stroke.points.length === 1)
    context.lineTo(first.x * canvas.width + 0.1, first.y * canvas.height + 0.1);
  context.stroke();
  context.restore();
}

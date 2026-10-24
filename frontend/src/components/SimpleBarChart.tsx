import * as React from 'react';
import { Box, Typography, useTheme } from '@mui/material';

export interface BarDatum {
  key: string;
  label: string;      // подпись на оси X
  value: number;
  tooltip: string;    // текст подсказки при наведении
}

interface Props {
  data: BarDatum[];
  height?: number;
  formatValue?: (v: number) => string;
  ariaLabel: string;
}

/**
 * Лёгкая столбчатая диаграмма на SVG (без библиотек): тонкие столбцы со скруглённым верхом,
 * приглушённая сетка, подсказка при наведении, подписи оси X с прореживанием.
 */
const SimpleBarChart: React.FC<Props> = ({ data, height = 220, formatValue = (v) => String(v), ariaLabel }) => {
  const theme = useTheme();
  const ref = React.useRef<HTMLDivElement>(null);
  const [width, setWidth] = React.useState(600);
  const [hover, setHover] = React.useState<number | null>(null);

  React.useEffect(() => {
    if (!ref.current) return;
    const observer = new ResizeObserver(entries => setWidth(Math.max(240, entries[0].contentRect.width)));
    observer.observe(ref.current);
    return () => observer.disconnect();
  }, []);

  const padding = { top: 12, right: 8, bottom: 26, left: 48 };
  const innerW = width - padding.left - padding.right;
  const innerH = height - padding.top - padding.bottom;
  const max = Math.max(1, ...data.map(d => d.value));

  // «Красивый» шаг сетки
  const rawStep = max / 4;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const step = [1, 2, 2.5, 5, 10].map(m => m * magnitude).find(s => s >= rawStep) ?? rawStep;
  const top = Math.ceil(max / step) * step;
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);

  const slot = innerW / Math.max(1, data.length);
  const gap = Math.min(6, Math.max(2, slot * 0.25));
  const barW = Math.max(2, Math.min(28, slot - gap));
  const labelEvery = Math.max(1, Math.ceil(data.length / Math.floor(innerW / 44)));

  const barColor = theme.palette.primary.main;
  const gridColor = theme.palette.divider;
  const textColor = theme.palette.text.secondary;
  const y = (v: number) => padding.top + innerH - (v / top) * innerH;

  const hovered = hover !== null ? data[hover] : null;
  const tooltipX = hover !== null ? padding.left + slot * hover + slot / 2 : 0;

  return (
    <Box ref={ref} sx={{ position: 'relative', width: '100%' }}>
      <svg width={width} height={height} role="img" aria-label={ariaLabel} style={{ display: 'block' }}>
        {ticks.map(t => (
          <g key={t}>
            <line x1={padding.left} x2={width - padding.right} y1={y(t)} y2={y(t)} stroke={gridColor} strokeWidth={1} />
            <text x={padding.left - 6} y={y(t)} textAnchor="end" dominantBaseline="middle" fontSize={11} fill={textColor}>
              {formatValue(t)}
            </text>
          </g>
        ))}
        {data.map((d, i) => {
          const x = padding.left + slot * i + (slot - barW) / 2;
          const h = Math.max(0, padding.top + innerH - y(d.value));
          const r = Math.min(4, barW / 2, h);
          // Столбец со скруглением только сверху, «стоит» на базовой линии
          const path = h <= 0 ? '' : `M${x},${y(0)} V${y(d.value) + r} Q${x},${y(d.value)} ${x + r},${y(d.value)} H${x + barW - r} Q${x + barW},${y(d.value)} ${x + barW},${y(d.value) + r} V${y(0)} Z`;
          return (
            <g key={d.key}>
              {path && <path d={path} fill={barColor} opacity={hover === null || hover === i ? 1 : 0.45} />}
              {i % labelEvery === 0 && (
                <text x={padding.left + slot * i + slot / 2} y={height - 8} textAnchor="middle" fontSize={11} fill={textColor}>
                  {d.label}
                </text>
              )}
              {/* Зона наведения шире самого столбца */}
              <rect
                x={padding.left + slot * i}
                y={padding.top}
                width={slot}
                height={innerH}
                fill="transparent"
                onMouseEnter={() => setHover(i)}
                onMouseLeave={() => setHover(null)}
                onTouchStart={() => setHover(i)}
              />
            </g>
          );
        })}
      </svg>
      {hovered && (
        <Box
          sx={{
            position: 'absolute',
            top: 0,
            left: Math.min(Math.max(tooltipX, 70), width - 70),
            transform: 'translateX(-50%)',
            bgcolor: 'background.paper',
            border: `1px solid ${theme.palette.divider}`,
            borderRadius: 2,
            px: 1.25,
            py: 0.5,
            boxShadow: 2,
            pointerEvents: 'none',
            whiteSpace: 'nowrap',
          }}
        >
          <Typography variant="caption" sx={{ whiteSpace: 'pre-line' }}>{hovered.tooltip}</Typography>
        </Box>
      )}
    </Box>
  );
};

export default SimpleBarChart;

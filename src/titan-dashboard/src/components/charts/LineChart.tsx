import {
  ResponsiveContainer,
  LineChart as RechartsLineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
} from 'recharts';

interface DataPoint {
  [key: string]: string | number;
}

interface LineChartProps {
  data: DataPoint[];
  dataKey?: string;
  xAxisKey?: string;
  color?: string;
  height?: number;
}

export function LineChart({
  data,
  dataKey = 'value',
  xAxisKey = 'timestamp',
  color = 'var(--color-accent)',
  height = 200,
}: LineChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <RechartsLineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
        <XAxis
          dataKey={xAxisKey}
          stroke="var(--color-text-secondary)"
          tick={{ fontSize: 12, fill: 'var(--color-text-secondary)' }}
        />
        <YAxis
          stroke="var(--color-text-secondary)"
          tick={{ fontSize: 12, fill: 'var(--color-text-secondary)' }}
        />
        <Tooltip
          contentStyle={{
            background: 'var(--color-bg-secondary)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-md)',
            color: 'var(--color-text-primary)',
          }}
        />
        <Line
          type="monotone"
          dataKey={dataKey}
          stroke={color}
          strokeWidth={2}
          dot={false}
          activeDot={{ r: 4 }}
        />
      </RechartsLineChart>
    </ResponsiveContainer>
  );
}

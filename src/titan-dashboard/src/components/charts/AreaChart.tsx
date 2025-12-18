import {
  ResponsiveContainer,
  AreaChart as RechartsAreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
} from 'recharts';

interface DataPoint {
  [key: string]: string | number;
}

interface AreaChartProps {
  data: DataPoint[];
  areas: {
    dataKey: string;
    color: string;
    name?: string;
  }[];
  xAxisKey?: string;
  height?: number;
  stacked?: boolean;
}

export function AreaChart({
  data,
  areas,
  xAxisKey = 'timestamp',
  height = 200,
  stacked = true,
}: AreaChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <RechartsAreaChart data={data}>
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
        <Legend
          wrapperStyle={{ color: 'var(--color-text-secondary)' }}
        />
        {areas.map((area) => (
          <Area
            key={area.dataKey}
            type="monotone"
            dataKey={area.dataKey}
            stackId={stacked ? '1' : undefined}
            stroke={area.color}
            fill={area.color}
            fillOpacity={0.3}
            name={area.name || area.dataKey}
          />
        ))}
      </RechartsAreaChart>
    </ResponsiveContainer>
  );
}

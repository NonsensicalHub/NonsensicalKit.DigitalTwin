using System;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 货位禁用规则：满足所有已启用约束的货位视为结构禁用（不显示、业务不可再打开）。
    /// 例：第一排下面几层 → 约束排[0,0] + 层[0,2]。
    /// </summary>
    [Serializable]
    public sealed class WarehouseSlotDisableRule
    {
        [Tooltip("关闭后本条规则不生效")]
        public bool Enabled = true;

        [Tooltip("是否按排(Row)过滤")]
        public bool ConstrainRow;

        public int RowMin;
        public int RowMax;

        [Tooltip("是否按列(Column)过滤")]
        public bool ConstrainColumn;

        public int ColumnMin;
        public int ColumnMax;

        [Tooltip("是否按层(Level)过滤")]
        public bool ConstrainLevel;

        public int LevelMin;
        public int LevelMax;

        [Tooltip("是否按深(Depth)过滤")]
        public bool ConstrainDepth;

        public int DepthMin;
        public int DepthMax;

        public bool HasAnyConstraint =>
            ConstrainRow || ConstrainColumn || ConstrainLevel || ConstrainDepth;

        public bool Matches(int level, int column, int row, int depth)
        {
            if (!Enabled || !HasAnyConstraint)
            {
                return false;
            }

            if (ConstrainRow && !InRange(row, RowMin, RowMax))
            {
                return false;
            }

            if (ConstrainColumn && !InRange(column, ColumnMin, ColumnMax))
            {
                return false;
            }

            if (ConstrainLevel && !InRange(level, LevelMin, LevelMax))
            {
                return false;
            }

            if (ConstrainDepth && !InRange(depth, DepthMin, DepthMax))
            {
                return false;
            }

            return true;
        }

        public static bool MatchesAny(
            WarehouseSlotDisableRule[] rules,
            int level,
            int column,
            int row,
            int depth)
        {
            if (rules == null || rules.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                WarehouseSlotDisableRule rule = rules[i];
                if (rule != null && rule.Matches(level, column, row, depth))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InRange(int value, int a, int b)
        {
            int min = a < b ? a : b;
            int max = a < b ? b : a;
            return value >= min && value <= max;
        }
    }
}

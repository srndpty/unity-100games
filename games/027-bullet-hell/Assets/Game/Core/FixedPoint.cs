using System;

namespace Game.Core
{
    /// <summary>
    /// 10 進の小数の文字列を、浮動小数点を通さずに整数の単位へ直す（Mono と .NET で同じ値にするため）。
    /// 値 = 仮数 / 10^桁数 とし、<c>値 × 分子 / 分母</c> を 0 から遠いほうへ四捨五入する。
    /// </summary>
    public static class FixedPoint
    {
        /// <summary>小数点以下の最大の桁数。</summary>
        public const int MaxFractionDigits = 6;

        /// <summary>整数部の最大の桁数。</summary>
        public const int MaxIntegerDigits = 6;

        /// <summary>
        /// <paramref name="text"/>（例：<c>-1.25</c>）を <c>値 × numerator / denominator</c> の整数へ直す。
        /// 書式が違う・桁が多すぎる・結果が int に収まらないときは false。
        /// </summary>
        public static bool TryParse(string text, long numerator, long denominator, out int value)
        {
            value = 0;
            if (text == null || text.Length == 0) { return false; }
            if (numerator <= 0 || denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(numerator), "分子と分母は正にしてください。"); }
            if (denominator > (1L << 20)) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母が大きすぎます。"); }

            var index = 0;
            var negative = false;
            if (text[0] == '-')
            {
                negative = true;
                index = 1;
            }

            long mantissa = 0;
            var integerDigits = 0;
            while (index < text.Length && IsDigit(text[index]))
            {
                mantissa = (mantissa * 10) + (text[index] - '0');
                integerDigits++;
                index++;
            }

            var fractionDigits = 0;
            if (index < text.Length && text[index] == '.')
            {
                index++;
                while (index < text.Length && IsDigit(text[index]))
                {
                    mantissa = (mantissa * 10) + (text[index] - '0');
                    fractionDigits++;
                    index++;
                }

                if (fractionDigits == 0) { return false; }
            }

            if (index != text.Length || integerDigits == 0) { return false; }
            if (integerDigits > MaxIntegerDigits || fractionDigits > MaxFractionDigits) { return false; }

            long scale = 1;
            for (var i = 0; i < fractionDigits; i++) { scale *= 10; }

            // 仮数 < 10^12、分子 <= 2^20 なので積は 10^18 程度で long に収まる。
            if (numerator > (1L << 20)) { throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "分子が大きすぎます。"); }
            var result = RoundDiv(mantissa * numerator, scale * denominator);
            if (negative) { result = -result; }
            if (result > int.MaxValue || result < int.MinValue) { return false; }
            value = (int)result;
            return true;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static long RoundDiv(long numerator, long denominator) => (numerator + (denominator / 2)) / denominator;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GlmSharp;

namespace VectorSlider
{
    /* Генератор случайных чисел xoroshiro128**
     * https://prng.di.unimi.it/
     * Код самого генератора: https://prng.di.unimi.it/xoroshiro128starstar.c
     * 
     * Так-же некоторые части кода были взяты из библиотеки JOML:
     * https://github.com/JOML-CI/JOML/blob/44177d8329ab824a9a88e118ab17a31c0fe9a692/src/org/joml/Random.java
     * (там используется генератор xoroshiro128+)
     */
    public class Xoroshiro128
    {
        private static readonly float INT_TO_FLOAT = BitConverter.ToSingle(BitConverter.GetBytes(864026624), 0);

        // Xorshiro128 state
        private ulong _s0, _s1;

        public Xoroshiro128(long seed)
        {
            var initRng = new SplitMix64(seed);
            _s0 = initRng.next();
            _s1 = initRng.next();
        }

        public Xoroshiro128(ulong s0, ulong s1)
        {
            _s0 = s0;
            _s1 = s1;
        }

        public Xoroshiro128 Clone()
        {
            return new Xoroshiro128(_s0, _s1);
        }

        // Reference: <a href="https://github.com/roquendm/JGO-Grabbag/blob/master/src/roquen/math/rng/PRNG.java">https://github.com/roquendm/</a>
        // Range: [0, 1)
        public float NextFloat()
        {
            return ((uint)NextInt() >> 8) * INT_TO_FLOAT;
        }

        // Reference: <a href="https://github.com/roquendm/JGO-Grabbag/blob/master/src/roquen/math/rng/PRNG.java">https://github.com/roquendm/</a>
        // Range: (0, 1]
        public float NextFloatNonZero()
        {
            return (((uint)NextInt() >> 8) + 1) * INT_TO_FLOAT;
        }

        /**
         * Reference: <a href="https://github.com/roquendm/JGO-Grabbag/blob/master/src/roquen/math/rng/PRNG.java">https://github.com/roquendm/</a>
         * 
         * @author roquendm
         * 
         * Возвращает значение от 0(включительно) до n(не включительно)
         */
        public int NextInt(int n)
        {
            // See notes in nextInt. This is
            // (on average) a better choice for
            // 64-bit VMs.
            long r = (long)((ulong)NextInt() >> 1);
            // sign doesn't matter here
            r = (r * n) >> 31;
            return (int)r;
        }

        public int NextInt()
        {
            return (int)(next() >> 32);
        }

        public ulong next()
        {
            var s0 = _s0;
            var s1 = _s1;
            var result = RotateLeft(s0 * 5, 7) * 9;

            s1 ^= s0;
            _s0 = RotateLeft(s0, 24) ^ s1 ^ (s1 << 16); // a, b
            _s1 = RotateLeft(s1, 37); // c

            return result;
        }

        /* Вызов этого метода эквивалентен 2^64 вызовам next()
         * можно использовать, чтобы легко распараллелить этот генератор на 2^64 других
         * каждый из этих генераторов будет генерировать свой собственный поток из 2^64 чисел
         */
        public void Jump()
        {
            generalJump(new[] { 0xdf900294d8f554a5UL, 0x170865df4b3201fcUL });
        }

        /* Вызов этого метода эквивалентен 2^96 вызовам next()
         * можно использовать, чтобы легко распараллелить этот генератор на 2^32 других
         * каждый из этих генераторов будет генерировать свой собственный поток из 2^96 чисел
         */
        public void LongJump()
        {
            generalJump(new[] { 0xd2a98b26625eee7b, 0xdddf9b1090aa7ac1 });
        }

        private void generalJump(ulong[] polynomial)
        {
            ulong s0 = 0;
            ulong s1 = 0;
            foreach (var bits in polynomial)
		        for (var b = 0; b < 64; b++)
                    {
                        if ((bits & (1UL << b)) > 0)
                        {
                            s0 ^= _s0;
                            s1 ^= _s1;
                        }
                        next();
                    }

            _s0 = s0;
            _s1 = s1;
        }

        private static ulong RotateLeft(ulong val, int count)
        {
            return (val << count) | (val >> (64 - count));
        }
    }

    /* Генератор случайных чисел SplitMix64
     * Используется для инициализации xoroshiro128**
     * Код взят из библиотеки JOML:
     * https://github.com/JOML-CI/JOML/blob/44177d8329ab824a9a88e118ab17a31c0fe9a692/src/org/joml/Random.java
     */
    public class SplitMix64
    {
        private ulong state;

        public SplitMix64(long seed)
        {
            state = (ulong)seed;
        }

        public ulong next()
        {
            var z = state += 0x9e3779b97f4a7c15L;
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9L;
            z = (z ^ (z >> 27)) * 0x94d049bb133111ebL;
            return z ^ (z >> 31);
        }
    }


    /// <summary>
    /// Класс для генерации различных случайных значений/выборов.
    /// Внутри использует класс Xoroshiro128.
    /// Максимальная длина генерируемой последовательности: 2^128 - 1
    /// </summary>
    public class GameRandom
    {
        private Xoroshiro128 rng;

        private float nextNormal;
        private bool haveNextNormal = false;

        /// <summary>
        /// Инициализирует генератор.
        /// Начальное значение берётся из System.Random, используя пустой конструктор
        /// </summary>
        public GameRandom()
        {
            var bytes = new byte[8];
            new Random().NextBytes(bytes);
            
            var seed = BitConverter.ToInt64(bytes, 0);
            rng = new Xoroshiro128(seed);
        }

        /// <summary>
        /// Инициализирует генератор.
        /// Начальное значение выводится из единственного аргумента seed
        /// </summary>
        public GameRandom(int seed)
        {
            rng = new Xoroshiro128(seed);
        }

        /// <summary>
        /// Инициализирует генератор.
        /// Начальное значение выводится из единственного аргумента seed
        /// </summary>
        public GameRandom(long seed)
        {
            rng = new Xoroshiro128(seed);
        }

        private GameRandom(Xoroshiro128 rng)
        {
            this.rng = rng;
        }

        /// <summary>
        /// Разделяет этот генератор на два идентичных.
        /// !!! Каждый будет генерировать одну и ту же последовательность
        /// </summary>
        /// <returns>Копия этого генератора</returns>
        public GameRandom Fork()
        {
            return new GameRandom(rng.Clone());
        }

        /// <summary>
        /// Разделяет этот генератор на count других.
        /// Каждый будет генерировать свою собственную последовательность.
        /// Так-же обновляет этот генератор так,
        /// чтобы он тоже генерировал свою отдельную последовательность
        /// </summary>
        /// <param name="count">Количество генераторов, которые нужно вернуть</param>
        /// <returns>Новые генераторы</returns>
        public GameRandom[] Parallelize(int count)
        {
            var ret = new GameRandom[count];
            for (var i = 0; i < count; i++)
            {
                var newRng = rng.Clone();
                rng.LongJump();

                ret[i] = new GameRandom(newRng);
            }

            return ret;
        }

        // ============ Методы для генерации int'ов ============
        
        /// <summary>
        /// Генерирует случайное число
        /// </summary>
        /// <returns>Случайное число в [int.MinValue, int.MaxValue]</returns>
        public int NextInt()
        {
            return rng.NextInt();
        }

        /// <summary>
        /// Генерирует случайное число
        /// </summary>
        /// <returns>Случайное число в [0, maxValue]</returns>
        public int NextInt(int maxValue)
        {
            return rng.NextInt(maxValue + 1);
        }

        /// <summary>
        /// Генерирует случайное число
        /// </summary>
        /// <returns>Случайное число в [minValue, maxValue]</returns>
        public int NextInt(int minValue, int maxValue)
        {
            var n = maxValue - minValue + 1;
            return minValue + rng.NextInt(n);
        }

        // ============ Методы для генерации enum'ов ============

        /// <summary>
        /// Выбирает одно из значений данного enum'а
        /// </summary>
        /// <returns>Значение из данного enum'а</returns>
        public T ChooseOption<T>() where T: Enum {
            var values = Enum.GetValues(typeof(T));
            var index = NextInt(values.Length - 1);
            return (T)values.GetValue(index);
        }

        /// <summary>
        /// Выбирает одно из значений данного enum'а
        /// </summary>
        /// <returns>Значение из данного enum'а</returns>
        public object ChooseOption(Type enumType)
        {
            var values = Enum.GetValues(enumType);
            var index = NextInt(values.Length - 1);
            return values.GetValue(index);
        }

        // ============ Методы для генерации float'ов ============

        /// <summary>
        /// Генерирует случайное дробное число
        /// </summary>
        /// <returns>Случайное число в [0, 1)</returns>
        public float NextFloat()
        {
            return rng.NextFloat();
        }

        /// <summary>
        /// Генерирует случайное дробное число
        /// </summary>
        /// <returns>Случайное число в [0, maxValue)</returns>
        public float NextFloat(float maxValue)
        {
            return rng.NextFloat() * maxValue;
        }

        /// <summary>
        /// Генерирует случайное дробное число
        /// </summary>
        /// <returns>Случайное число в [minValue, maxValue)</returns>
        public float NextFloat(float minValue, float maxValue)
        {
            var range = maxValue - minValue;
            return minValue + range * rng.NextFloat();
        }

        // ============ Методы для генерации float'ов из различных распределений ============

        /// <summary>
        /// Берёт случайное число из плоского распределения.
        /// Эквивалентно вызову NextFloat()
        /// </summary>
        /// <returns>Случайное число</returns>
        public float Uniform()
        {
            return NextFloat();
        }

        /// <summary>
        /// Берёт случайное число из плоского распределения.
        /// Эквивалентно вызову NextFloat(minValue, maxValue)
        /// </summary>
        /// <param name="minValue">Нижняя граница распределения</param>
        /// <param name="maxValue">Верхняя граница распределения</param>
        /// <returns>Случайное число</returns>
        public float Uniform(float minValue, float maxValue)
        {
            return NextFloat(minValue, maxValue);
        }

        /// <summary>
        /// Берёт случайное число из нормального распределения.
        /// Эквивалентно вызову Normal(0, 1)
        /// </summary>
        /// <returns>Случайное число</returns>
        public float Normal()
        {
            if (haveNextNormal)
            {
                haveNextNormal = false;
                return nextNormal;
            }

            var randoms = Normal2();

            nextNormal = randoms.x;
            haveNextNormal = true;
            return randoms.y;
        }

        /// <summary>
        /// Берёт случайное число из нормального распределения
        /// </summary>
        /// <param name="mean">Среднее значение распределения</param>
        /// <param name="standardDeviation">Среднеквадратическое отклонение</param>
        /// <returns>Случайное число</returns>
        public float Normal(float mean, float standardDeviation)
        {
            return mean + standardDeviation * Normal();
        }

        // ============ Методы для генерации vec2'ов из различных распределений ============

        /// <summary>
        /// Берёт случайные числа из плоского распределения
        /// </summary>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Uniform2()
        {
            return new vec2(NextFloat(), NextFloat());
        }

        /// <summary>
        /// Берёт случайные числа из плоского распределения
        /// </summary>
        /// <param name="minValue">Нижняя граница распределения</param>
        /// <param name="maxValue">Верхняя граница распределения</param>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Uniform2(float minValue, float maxValue)
        {
            return new vec2(NextFloat(minValue, maxValue), NextFloat(minValue, maxValue));
        }

        /// <summary>
        /// Берёт случайные числа из плоских распределений
        /// </summary>
        /// <param name="minValue">Нижние границы распределения</param>
        /// <param name="maxValue">Верхнте границы распределения</param>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Uniform2(vec2 minValue, vec2 maxValue)
        {
            return new vec2(NextFloat(minValue.x, maxValue.y), NextFloat(minValue.x, maxValue.y));
        }

        /// <summary>
        /// Берёт случайные числа из нормального распределения
        /// </summary>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Normal2()
        {
            // Формула для нахождения чисел в нормальном распределении
            // https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform

            // При u1 = 0 результатом формулы будет точка (±∞, ±∞)
            // При u1 = 1, это будет точка (0, 0)
            // Второй результат гораздо полезнее
            // NextFloatNonZero никода не вернёт ровно 0, но может вернуть 1
            // (в отличии от NextFloat, для которого это наоборот)
            var u1 = rng.NextFloatNonZero();
            var u2 = rng.NextFloatNonZero();

            // TODO: Убрать преобразования из double и только высчитывать значения во float
            var r = (float)Math.Sqrt(-2 * Math.Log(u1));
            var theta = 2 * (float)Math.PI * u2;

            var z0 = r * (float)Math.Cos(theta);
            var z1 = r * (float)Math.Sin(theta);

            return new vec2(z0, z1);
        }

        /// <summary>
        /// Берёт случайные числа из нормального распределения
        /// </summary>
        /// <param name="mean">Среднее значение распределения</param>
        /// <param name="standardDeviation">Среднеквадратическое отклонение</param>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Normal2(float mean, float standardDeviation)
        {
            return new vec2(mean, mean) + standardDeviation * Normal2();
        }

        /// <summary>
        /// Берёт случайные числа из нормальных распределений
        /// </summary>
        /// <param name="mean">Средние значения распределений</param>
        /// <param name="standardDeviation">Среднеквадратическые отклонения</param>
        /// <returns>Два случайных числа внутри vec2</returns>
        public vec2 Normal2(vec2 mean, vec2 standardDeviation)
        {
            return mean + standardDeviation * Normal2();
        }
    }
}

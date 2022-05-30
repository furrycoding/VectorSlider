using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GlmSharp;

namespace VectorSlider
{
    // Для столкновений используется DSDF - Directional Signed Distance Field
    // По сути это два поля - одно даёт расстояние до ближайшего препятствия со знаком
    // (так называемое SDF, такая штука используется во многих областях - например для отрисовки шрифтов)
    // (в отличие от классического SDF, здесь все значения имеют противоположный знак)
    // Но кроме расстояния, для обработки столкновений,
    // нам ещё нужно направление - куда нужно двигать объект, чтобы он перестал сталкиваться с уровнем
    // Для этого нужно второе поле - оно по сути и даёт это направление
    // Таким образом, если мы просчитываем столкновения точки, нам всего-лишь надо выполнить
    // position += direction * Math.Max(distance, 0);
    // Где position - текущее положение точки
    // direction и distance - значения, взятые из этого поля
    
    
    // Класс, отвечающий за уровень, в котором машина ездит и с которым сталкивается
    internal class OldCarLevel : Component, IRenderable, ICarLevel
    {
        private List<vec4> circles = new List<vec4>();

        // Возвращает кортежи из двух элементов:
        // Вектора положения окружности и её радиуса
        public IEnumerable<(vec2, float)> Circles
        {
            get
            {
                foreach (var circle in circles)
                    yield return (circle.xy, circle.z);
            }
        }

        public void AddCircle(vec2 position, float radius, bool solidInside)
        {
            circles.Add(new vec4(position, radius, solidInside ? 1 : -1));
        }

        public (float, vec2) Collide(vec2 position, float colliderRadius)
        {
            var minDistance = -1e9f;
            var minDirection = new vec2();

            foreach (var circle in circles)
            {
                var circleRadius = circle.z;
                var relativePos = position - circle.xy;
                var outside = circle.w;

                var len = relativePos.Length;
                var distance = outside * (circleRadius - len) + colliderRadius;
                var direction = outside * relativePos / Math.Max(1e-5f, len);

                if (Math.Abs(distance) > Math.Abs(minDistance))
                    continue;

                minDistance = distance;
                minDirection = direction;
            }

            return (minDistance, minDirection);
        }

        public void Render(Graphics g)
        {
            var pen1 = new Pen(Color.LimeGreen, 0.08f);
            var pen2 = new Pen(Color.Red, 0.1f);
            var pen3 = new Pen(Color.Green, 0.1f);

            g.DrawLine(pen2, 0f, 0f, 1f, 0f);
            g.DrawLine(pen3, 0f, 0f, 0f, 1f);
            foreach (var (pos, radius) in Circles)
                g.DrawArc(pen1, pos.x - radius, pos.y - radius, 2 * radius, 2 * radius, 0, 360);
        }
    }

    internal class CarLineLevel : Component, IRenderable, ICarLevel
    {
        public vec2[][] lines;

        public IEnumerable<(vec2, vec2)> GetLinePoints()
        {
            foreach (var connectedLine in lines)
            {
                System.Diagnostics.Debug.Assert(connectedLine.Length >= 2);

                var prevPoint = connectedLine[0];
                for (var i = 1; i < connectedLine.Length; i++)
                {
                    var p0 = prevPoint;
                    var p1 = connectedLine[i];
                    prevPoint = p1;

                    yield return (p0, p1);
                }
            }
        }

        public static void LineDSDF(vec2 sample, vec2 p0, vec2 p1,
            ref vec2 direction, ref float distance, ref float minDistanceSqr)
        {
            var lineVec = p1 - p0;
            var pointVec = sample - p0;

            var div = glm.Dot(lineVec, lineVec);
            System.Diagnostics.Debug.Assert(div > 1e-7f);
            var t = glm.Dot(pointVec, lineVec) / div;
            t = t.Clamp(0, 1);

            var closestPointToSample = pointVec - t * lineVec;
            var sqrDist = closestPointToSample.LengthSqr;
            if (sqrDist >= minDistanceSqr)
                return;

            var dist = closestPointToSample.Length;
            dist = Math.Max(dist, 1e-5f);

            var u = glm.Cross(pointVec, lineVec);
            dist *= glm.Sign(u);

            direction = closestPointToSample / dist;
            distance = -dist;
            minDistanceSqr = sqrDist;
        }

        public void SampleDSDF(vec2 sample, out vec2 direction, out float distance)
        {
            direction = new vec2();
            distance = -1e9f;
            var minDistanceSqr = 1e9f;

            foreach (var (p0, p1) in GetLinePoints())
                LineDSDF(sample, p0, p1, ref direction, ref distance, ref minDistanceSqr);
        }

        public (float, vec2) Collide(vec2 center, float radius)
        {
            vec2 direction;
            float distance;
            SampleDSDF(center, out direction, out distance);
            distance += radius;

            return (distance, direction);
        }

        public void Render(Graphics g)
        {
            var pen1 = new Pen(Color.LimeGreen, 0.08f);

            foreach (var (p0, p1) in GetLinePoints())
                g.DrawLine(pen1, p0.x, p0.y, p1.x, p1.y);
        }
    }

    internal class SmoothedLineLevel : Component, IRenderable, ICarLevel
    {
        private struct Line
        {
            public vec2 StartPoint;
            public vec2 Tangent, NextTangent;
            public float Length;
            public float CornerRadius;
            public vec2 CirclePosition;
            public float CircleRadius;
            public float Sign;
        }

        private Line[] lines;

        /// <summary>
        /// lines - массив цепочек линий. 
        /// Каждая цепочка представляет собой массив vec3, 
        /// где первые два компонента обозначают положение вершины, 
        /// а третий - расстояние до точки косания окружности,
        /// вписанной в угол, образованный этой вершиной.
        /// </summary>
        public void InitializeLines(vec3[][] rawLines)
        {
            var bakedLines = new List<Line>();
            var line = new Line();

            foreach (var connectedLine in rawLines)
            {
                System.Diagnostics.Debug.Assert(connectedLine.Length >= 3);

                for (var i = 2; i < connectedLine.Length; i++)
                {
                    var l0 = connectedLine[i - 2];
                    var l1 = connectedLine[i - 1];
                    var l2 = connectedLine[i - 0];

                    var (p0, r0, p1, r1, p2, r2) = (l0.xy, l0.z, l1.xy, l1.z, l2.xy, l2.z);

                    var v0 = (p1 - p0).NormalizedSafe;
                    var v1 = (p2 - p1).NormalizedSafe;
                    var v_mid = (v1 - v0).NormalizedSafe;

                    var sin = glm.Cross(v0, v_mid);
                    var cos = -glm.Dot(v0, v_mid);
                    var circleRadius = glm.Abs(sin) * r1 / cos;
                    var circlePos = p1 + v_mid * r1 / cos;

                    line.StartPoint = p0 + v0 * r0;
                    line.Tangent = v0;
                    line.NextTangent = v1;
                    line.Length = glm.Dot(p1 - p0, v0) - r0;
                    line.CornerRadius = r1;
                    line.CirclePosition = circlePos;
                    line.CircleRadius = circleRadius;
                    line.Sign = glm.Sign(sin);
                    bakedLines.Add(line);
                }
            }

            lines = bakedLines.ToArray();
        }

        public void SampleDSDF(vec2 sample, out vec2 direction, out float distance)
        {
            direction = new vec2();
            distance = -1e9f;
            var minDistanceSqr = 1e9f;

            foreach (var line in lines)
            {
                var cornerPos = line.StartPoint + line.Tangent * line.Length;
                var cornerToSample = sample - cornerPos;

                // Проверка на то, находимся ли мы в районе, где угловая окружность будет влиять на результат
                var check0 = glm.Dot(cornerToSample, line.Tangent) + line.CornerRadius;
                var check1 = glm.Dot(cornerToSample, line.NextTangent) - line.CornerRadius;
                if ((check0 < 0) || (check1 > 0))
                {
                    // Если нет, то на результат модет влиять линия
                    var check2 = glm.Dot(sample - line.StartPoint, line.Tangent);
                    if (check2 > 0)
                        CarLineLevel.LineDSDF(
                            sample, line.StartPoint, cornerPos,
                            ref direction, ref distance, ref minDistanceSqr
                        );
                    continue;
                }

                var relativePos = sample - line.CirclePosition;
                var len = relativePos.Length;

                var tmpDist = line.CircleRadius - len;
                if (minDistanceSqr < tmpDist * tmpDist)
                    continue;

                direction = line.Sign * relativePos / Math.Max(1e-5f, len);
                distance = line.Sign * tmpDist;
                minDistanceSqr = tmpDist * tmpDist;
            }
        }

        public (float, vec2) Collide(vec2 center, float radius)
        {
            vec2 direction;
            float distance;
            SampleDSDF(center, out direction, out distance);
            distance += radius;

            return (distance, direction);
        }

        public void Render(Graphics g)
        {
            var pen1 = new Pen(Color.LimeGreen, 0.08f);

            foreach (var line in lines)
            {
                var l0 = line.StartPoint;
                var l1 = l0 + line.Tangent * (line.Length - line.CornerRadius);
                g.DrawLine(pen1, l0.x, l0.y, l1.x, l1.y);

                var r = line.CircleRadius;
                if (glm.Abs(r) < 1e-5)
                    continue;

                var arcCorner = line.CirclePosition - new vec2(r);

                var v0 = -line.Tangent;
                var v1 = line.NextTangent;
                var angle0 = glm.Degrees((float)glm.Angle(v0));
                var angle1 = glm.Degrees((float)glm.Angle(v1));
            g.ScaleTransform(scale, scale);
                if (line.Sign > 0)
                {
                    var tmp = angle1;
                    angle1 = angle0;
                    angle0 = tmp;
                }
            foreach (var (pos, radius) in level.Circles)
                angle0 = (angle0 + 270) % 360;
                angle1 = (angle1 + 90) % 360;

                g.DrawArc(pen1, arcCorner.x, arcCorner.y, 2 * r, 2 * r, angle0, angle1 - angle0);
            }
        }
    }

    internal interface ICarLevel
    {
        (float, vec2) Collide(vec2 center, float radius);
            g.DrawRectangle(pen1, -0.5f * car.Length, -0.5f * car.Width, car.Length, car.Width);
        }
    }
}

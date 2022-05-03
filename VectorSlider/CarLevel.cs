using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Windows.Forms;
using System.Drawing;

using GlmSharp;


namespace VectorSlider
{
    // Класс, отвечающий за обработку ввода и передаче его классу физики
    internal class CarInput : Component
    {
        // Ввод пользователя
        public bool forwards, backwards, left, right;

        // Обработка ввода
        public float InputSmoothing = 0.1f;

        // Обработанный ввод пользователя
        private bool inReverse;
        private float turn, gas, brake;

        private CarPhysics car;

        public CarInput(CarPhysics car)
        {
            this.car = car;
        }

        protected override void OnInitialize()
        {
            World.Input.AddKeyBinding("car/forwards", Keys.W);
            World.Input.AddKeyBinding("car/backwards", Keys.S);
            World.Input.AddKeyBinding("car/left", Keys.A);
            World.Input.AddKeyBinding("car/right", Keys.D);

            World.Input.AddHanlder("car/forwards", doubleHandler: pressed => forwards = pressed);
            World.Input.AddHanlder("car/backwards", doubleHandler: pressed => backwards = pressed);
            World.Input.AddHanlder("car/left", doubleHandler: pressed => left = pressed);
            World.Input.AddHanlder("car/right", doubleHandler: pressed => right = pressed);
        }

        public override void Update(float dt)
        {
            // Пруобразуем состояние кнопок влево/вправо в число -1, 0 или 1
            var turnTarget = 0;
            if (left) turnTarget -= 1;
            if (right) turnTarget += 1;

            // Преобразуем кнопки вперёд/назад в управление газом и тормозом
            // Газ принимает значение 1(вперёд), 0(нет газа) и -1(назад)
            var gasTarget = 0;
            // Тормоз принимает значение либо 1, либо 0
            var brakeTarget = 0;

            if (forwards) gasTarget += 1;
            if (backwards) gasTarget -= 1;
            if (inReverse ? (gasTarget > 0) : (gasTarget < 0))
            {
                if (glm.LengthSqr(car.velocity) < 1e-4f)
                    inReverse = !inReverse;
                else
                {
                    gasTarget = 0;
                    brakeTarget = 1;
                }
            }

            // Сделаем ввод более плавным
            var alpha = (float)Math.Pow(InputSmoothing, dt);
            turn = alpha * turn + (1 - alpha) * turnTarget;
            gas = alpha * gas + (1 - alpha) * gasTarget;
            brake = alpha * brake + (1 - alpha) * brakeTarget;

            car.turn = turn;
            car.gas = gas;
            car.brake = brake;
        }
    }

    // Класс, отвечающий за саму физику машины, как она двигается и сталкивается
    internal class CarPhysics : Component, IRenderable
    {
        // Свойства машины
        public float TopSpeed = 8f;
        public float ReverseSpeed = 3f;
        public float GasAcceleration = 12f;
        public float BrakeDeceleration = 14f;
        public float WheelFriction = 14f;
        public float TurnSpeed = 1f;
        public float WallSlideBoost = 1.9f;

        public float Width = 0.4f;
        public float Length = 1f;

        // Текущее состояние машины
        public vec2 position = new vec2(3, 3);
        public vec2 velocity = new vec2();
        public float rotation = 0;
        public int collidingCorner { get; private set; }

        // Управление машиной
        public float turn, gas, brake;

        private CarLevel level;

        public vec2 ForwardDirection => glm.Rotated(new vec2(1, 0), rotation);
        public vec2 Left => glm.Rotated(new vec2(0, 1), rotation);


        public CarPhysics(CarLevel level)
        {
            this.level = level;
        }

        private bool Collide(CarLevel level)
        {
            var distance = -1f;
            var direction = new vec2();
            var curIdx = -1;

            var (dist, dir) = level.Collide(position, 0.2f);
            if (dist > distance)
            {
                distance = dist;
                direction = dir;
            }
            var idx = 0;
            foreach (var corner in GetWorldSpaceCorners())
            {
                (dist, dir) = level.Collide(corner, 0.0f);
                if (dist > distance)
                {
                    distance = dist;
                    direction = dir;
                    curIdx = idx;
                }
                idx++;
            }

            if (distance <= 0)
                return false;

            position += distance * direction;
            velocity += direction * Math.Max(0, -glm.Dot(velocity, direction));
            collidingCorner = curIdx;
            return true;
        }

        public override void Update(float dt)
        {
            var invDt = 1f / Math.Max(1e-4f, dt);

            // Нам нужны векторы направленые вперёд и влево относительно машины
            var forward = ForwardDirection;
            var left = new vec2(-forward.y, forward.x);

            // Найдём проекцию скорости на эти вектора
            // Таким образом получим скорость колёс и скорость заноса
            var wheelSpeed = glm.Dot(forward, velocity);
            var sideSpeed = glm.Dot(left, velocity);

            // Ускорение из-за педали газа
            var targetSpeed = (gas < 0) ? Math.Min(-ReverseSpeed, wheelSpeed) : Math.Max(TopSpeed, wheelSpeed);
            var accel1 = (targetSpeed - wheelSpeed) * invDt;
            var maxDV = Math.Abs(gas) * GasAcceleration;
            accel1 = accel1.Clamp(-maxDV, maxDV);

            // Ускорение из-за тормоза
            var accel2 = -brake * wheelSpeed * invDt;
            accel2 = accel2.Clamp(-BrakeDeceleration, BrakeDeceleration);

            // Общее ускорение из-за колёс
            var wheelAccel = accel1 + accel2;
            // Сила трения колёс, препятствующая заносу
            var sideAccel = (-sideSpeed * invDt).Clamp(-WheelFriction, WheelFriction);
            // Собираем всё в вектор ускорения
            var totalAccel = forward * wheelAccel + left * sideAccel;

            // Интегрируем ускорение, чтобы найти скорость и положение
            velocity += totalAccel * dt;
            position += velocity * dt + totalAccel * dt * dt / 2;

            // Также обновляем вращение
            rotation += TurnSpeed * dt * turn * wheelSpeed;
            rotation %= (float)(2 * Math.PI);

            // Мы обновили положение - теперь надо проверить сталкиваемся ли мы со стенами?
            var i = 0;
            for (; i < 16; i++)
                if(!Collide(level))
                    break;
            if (i == 0) {
                collidingCorner = -1;
                return;
            }

            // Если мы столкнулись со стеной, то добавим ускорения машине
            var boostMultiplier = (float)Math.Pow(WallSlideBoost, dt);
            velocity *= boostMultiplier;
        }

        public void Render(Graphics g)
        {
            var pen1 = new Pen(Color.LimeGreen, 0.08f);

            g.TranslateTransform(position.x, position.y);
            g.RotateTransform(glm.Degrees(rotation));
            g.DrawRectangle(pen1, -0.5f * Length, -0.5f * Width, Length, Width);
        }

        public vec2[] GetWorldSpaceCorners()
        {
            var halfSize = 0.5f * new vec2(Length, Width);
            var diagHalfSize = 0.5f * new vec2(-Length, Width);

            return new vec2[]
            {
                position + glm.Rotated(-halfSize, rotation),
                position + glm.Rotated(-diagHalfSize, rotation),
                position + glm.Rotated(halfSize, rotation),
                position + glm.Rotated(diagHalfSize, rotation)
            };
        }
    }

    // Класс, отвечающий за уровень, в котором машина ездит и с которым сталкивается
    internal class CarLevel : Component, IRenderable
    {
        private List<vec4> circles = new List<vec4>();

        // Возвращает кортежи из двух элементов:
        // Вектора положения окружности и её радиуса
        public IEnumerable<(vec2, float)> Circles
        {
            get {
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

    // Класс, отвечающий за камеру, следующую за машиной
    internal class CarCamera : Component, ICamera
    {
        public vec2 camPos = new vec2();
        public float camZoom = 0.1f;

        private CarPhysics car;

        public CarCamera(CarPhysics car)
        {
            this.car = car;
        }

        public void ApplyTransform(Graphics g)
        {
            var bounds = g.VisibleClipBounds;
            var scale = bounds.Height * camZoom;
            g.TranslateTransform(0.5f * bounds.Width, 0.5f * bounds.Height);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-camPos.x, -camPos.y);
        }

        public override void Update(float dt)
        {
            camPos = 0.95f * camPos + 0.05f * car.position;
        }
    }

    // Класс, отвечающий за частицы связанные с столкновением машины со стеной
    internal class CarSlidingDust : Component
    {
        private ParticleSystem particles;

        private CarPhysics car;

        public CarSlidingDust(CarPhysics car)
        {
            this.car = car;
        }

        protected override void OnInitialize()
        {
            var particleBrush = Brushes.LightGreen;
            particles = World.AddComponent(new ParticleSystem(
                (rng, p, pos, dir) =>
                {
                    p.velocity = rng.Normal2(5 * dir.xy, new vec2(0.9f));
                    p.lifetime = rng.Normal(0.7f, 0.5f);
                },
                (g, p) =>
                {
                    var x = Math.Min(p.lifetime, 1);
                    var sz = 0.01f + 0.02f * x;
                    g.FillEllipse(particleBrush, p.position.x - sz, p.position.y - sz, 2 * sz, 2 * sz);
                }
            ));
        }

        public override void Update(float dt)
        {
            if ((car.collidingCorner >= 0) && (car.velocity.LengthSqr > 25f))
            {
                var corner = car.GetWorldSpaceCorners()[car.collidingCorner];
                var dir = -car.velocity.Normalized;
                var emitCount = (int)(1f * car.velocity.Length);
                particles.Emit(emitCount, corner, new vec4(dir));
            }
        }
    }

    // Класс, инициализирующий все основные компоненты игры
    internal class CarGame : Component
    {
        protected override void OnInitialize()
        {
            var level = World.AddComponent<CarLevel>();
            level.AddCircle(new vec2(0, 0), 5, false);
            level.AddCircle(new vec2(-2.5f, 0), 1.5f, true);
            level.AddCircle(new vec2(-0.8f, 0.9f), 0.7f, true);

            var car = World.AddComponent(new CarPhysics(level));
            car.position = new vec2(0f, 0f);

            World.AddComponent(new CarCamera(car));
            World.AddComponent(new CarSlidingDust(car));
            World.AddComponent(new CarInput(car));

            Destroy();
        }
    }
}

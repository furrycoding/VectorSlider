using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorSlider
{
    internal class World
    {
        private List<Component> components = new List<Component>();
        public readonly InputManager Input = new InputManager();

        public T AddComponent<T>() where T : Component, new()
        {
            return AddComponent(new T());
        }

        public T AddComponent<T>(T c) where T: Component
        {
            components.Add(c);
            c.Initialize(this);
            return c;
        }

        public IEnumerable<T> GetComponents<T>() where T : Component
        {
            foreach (var comp in components)
                if (comp is T ret)
                    yield return ret;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (var comp in components)
                if (comp is T ret)
                    return ret;
            return null;
        }

        public void RemoveComponent(Component c)
        {
            if (!c.Destroyed)
                c.Destroy();
            components.Remove(c);
        }

        public void Update(float dt)
        {
            foreach (var comp in components)
                comp.Update(dt);
        }

        public void Render(System.Drawing.Graphics g)
        {
            foreach (var comp in components)
                if (comp is ICamera cam) {
                    cam.ApplyTransform(g);
                    break;
                }

            var tf = g.Transform;
            foreach (var comp in components)
                if (comp is IRenderable renderable) {
                    renderable.Render(g);
                    g.Transform = tf;
                }
        }
    }

    internal abstract class Component
    {
        public bool Destroyed { get; private set; } = false;

        protected World World { get; private set; } = null;

        public void Initialize(World owner)
        {
            if (Destroyed)
                throw new InvalidOperationException("Can't re-initialize a Component");

            World = owner;
            OnInitialize();
        }

        public void Destroy()
        {
            OnDestroy();
            Destroyed = true;
            World.RemoveComponent(this);
            World = null;
        }

        protected virtual void OnInitialize() { }

        protected virtual void OnDestroy() { }

        public virtual void Update(float dt) { }
    }

    internal interface IRenderable
    {
        void Render(System.Drawing.Graphics g);
    }

    internal interface ICamera
    {
        void ApplyTransform(System.Drawing.Graphics g);
    }
}

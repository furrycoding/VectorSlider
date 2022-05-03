using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using GlmSharp;

namespace VectorSlider
{
    internal class ParticleSystem : Component, IRenderable
    {
        private int aliveParticles = 0;
        private List<Particle> particles;

        private GameRandom rng;

        private ParticleInitializer particleInitializer;
        private ParticleDrawer particleDrawer;
        
        public ParticleSystem(ParticleInitializer particleInitializer, ParticleDrawer particleDrawer)
        {
            this.particleInitializer = particleInitializer;
            this.particleDrawer = particleDrawer;

            rng = new GameRandom();
            particles = new List<Particle>();
            AllocateParticles(1024);
        }

        public void Emit(int count, vec2 position, vec4 parameters=new vec4(), int? rngSeed=null)
        {
            if(particleInitializer == null)
                return;

            var curRng = rng;
            if (rngSeed.HasValue)
                curRng = new GameRandom(rngSeed.Value);

            var needToAllocate = aliveParticles + count - particles.Count;
            if (needToAllocate > 0)
                AllocateParticles(Math.Max(1024, needToAllocate));
            
            for (var idx = aliveParticles; idx < aliveParticles + count; idx++) {
                var p = particles[idx];
                particleInitializer(curRng, p, position, parameters);
                p.alive = true;
                p.position = position;
            }

            aliveParticles += count;
        }

        public void Render(Graphics g)
        {
            if (particleDrawer == null)
                return;

            for (var i = 0; i < aliveParticles; i++)
                particleDrawer(g, particles[i]);
        }

        public override void Update(float dt)
        {
            var i = 0;
            while (i < aliveParticles)
            {
                var p = particles[i];
                System.Diagnostics.Debug.Assert(p.alive);

                p.lifetime -= dt;
                if (p.lifetime <= 0)
                {
                    aliveParticles--;
                    particles[aliveParticles].CopyTo(p);
                    particles[aliveParticles].alive = false;
                    continue;
                }

                p.position += p.velocity * dt;

                i++;
            }
        }

        private void AllocateParticles(int count)
        {
            for (var i = 0; i < count; i++)
                particles.Add(new Particle());
        }

        public delegate void ParticleInitializer(GameRandom rng, Particle p, vec2 position, vec4 parameters);
        public delegate void ParticleDrawer(Graphics g, Particle p);
    }

    internal class Particle
    {
        public bool alive;
        public float lifetime;
        public vec2 position;
        public vec2 velocity;

        public vec4 properties;

        public void CopyTo(Particle other)
        {
            other.alive = alive;
            other.position = position;
            other.velocity = velocity;
            other.properties = properties;
            other.lifetime = lifetime;
        }
    }
}

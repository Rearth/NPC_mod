using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRageMath;
using IMySlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;

namespace NPCMod {
    public partial class NPCBasicMover {
        public IMyEntity ActiveEnemy {
            set { activeEnemy = value; }
        }

        private readonly int range;
        private readonly float damage;
        private readonly float attacksPerSecond;
        private float attackWaitTime;
        private MyParticleEffect impactEffect;
        private MyParticleEffect launchEffect;
        private int shotAnimDur = 0;

        private void updateCombat() {
            checkParticles();
            if (activeEnemy == null) {
                movementMode = NPCDataAnimator.MovementMode.Standing;
                return;
            }
            if (attackWaitTime < attacksPerSecond) {
                attackWaitTime += 1 / 60f;
                return;
            }
            
            if (Vector3.Distance(animator.grid.GetPosition(), activeEnemy.WorldAABB.Center) < range) {

                movementMode = NPCDataAnimator.MovementMode.Attacking;
                
                var target = targetRandomBlock();
                var didShoot = fireAt(target);
                if (didShoot) {
                    attackWaitTime = 0;
                }
            }
            else {
                movementMode = NPCDataAnimator.MovementMode.Walking;
            }
        }

        private bool hasDirectLOS(Vector3 pos, IMyEntity targetEntity) {
            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(animator.grid.GetPosition(), pos, hits);

            if (hits.Count > 0) {
                var hitInfo = hits[0];
                var entity = hitInfo.HitEntity;

                var clearLOS = entity.Equals(targetEntity);
                return clearLOS;
            }

            return false;
        }

        private Vector3 targetRandomBlock() {
            var grid = activeEnemy as IMyCubeGrid;
            if (grid == null) return activeEnemy.WorldAABB.Center;

            for (int i = 0; i < 3; i++) {
                if (targetedBlock == null) {
                    var blocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    targetedBlock = blocks[MyRandom.Instance.Next(blocks.Count - 1)];
                }

                if (targetedBlock != null) {
                    var pos = grid.GridIntegerToWorld(targetedBlock.Position);
                    if (hasDirectLOS(pos, activeEnemy))
                        return pos;
                }
            }


            return activeEnemy.WorldAABB.Center;
        }

        private bool fireAt(Vector3 target) {
            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(animator.grid.GetPosition(), target, hits);
            //drawDebugLine(animator.grid.GetPosition(), target);

            if (hits.Count > 0) {
                var hitInfo = hits[0];
                var entity = hitInfo.HitEntity;

                var clearLOS = entity.Equals(activeEnemy);
                if (!clearLOS) return false;

                var grid = entity as MyCubeGrid;
                IMyDestroyableObject destroyable;

                spawnImpactParticle(hitInfo.Position, hitInfo.Normal);
                //spawnLaunchParticle(animator.grid.GetPosition(), hitInfo.Position - animator.grid.GetPosition());
                shotAnimDur = 0;

                if (grid != null) {
                    if (grid.Physics == null || !grid.Physics.Enabled || !grid.BlocksDestructionEnabled) return false;

                    var block = grid.GetTargetedBlock(hitInfo.Position) as IMySlimBlock;

                    if (block == null) return false;

                    var myHitInfo = new MyHitInfo {Position = hitInfo.Position, Normal = hitInfo.Normal};
                    weaponLineAnimator.addAnim(animator.grid.GetPosition(), hitInfo.Position);
                    grid.DoDamage(damage, myHitInfo, null, animator.grid.EntityId);
                    return true;
                } else if ((destroyable = entity as IMyDestroyableObject) != null) {
                    var myHitInfo = new MyHitInfo {Position = hitInfo.Position, Normal = hitInfo.Normal};
                    destroyable.DoDamage(damage, MyDamageType.Bullet, true, myHitInfo, animator.grid.EntityId);
                    return true;
                }
            }

            return false;
        }

        private void checkParticles() {
            shotAnimDur++;
            if (impactEffect != null && shotAnimDur > 5) {
                impactEffect.StopEmitting();
            }

            if (launchEffect != null && shotAnimDur > 25) {
                launchEffect.Stop();
            }
        }

        private void OnSmokeEffectDelete(object sender, EventArgs eventArgs) {
            launchEffect = null;
        }

        private void spawnLaunchParticle(Vector3D pos, Vector3D dir) {
            dir.Normalize();
            var matrix = MatrixD.CreateFromTransformScale(Quaternion.CreateFromForwardUp(dir, Vector3.Up), pos,
                Vector3D.One);

            if (launchEffect != null) return;
            if (!((MyAPIGateway.Session.Player.GetPosition() - pos).Length() < 150)) return;
            if (MyParticlesManager.TryCreateParticleEffect((int) MyParticleEffectsIDEnum.Smoke_SmallGunShot,
                out launchEffect)) {
                launchEffect.WorldMatrix = matrix;
                launchEffect.UserBirthMultiplier = 15;
                launchEffect.OnDelete += OnSmokeEffectDelete;
            }
        }

        private void spawnImpactParticle(Vector3D pos, Vector3 dir) {
            dir.Normalize();
            var matrix = MatrixD.CreateFromTransformScale(Quaternion.CreateFromForwardUp(-dir, Vector3.Up), pos,
                Vector3D.One);

            MyParticleEffect effect;

            if (impactEffect == null && MyParticlesManager.TryCreateParticleEffect(32, out effect, false)) {
                effect.WorldMatrix = matrix;
                effect.Length = 0.1f;
                effect.Loop = false;
                effect.DurationMin = 0.1f;
                effect.DurationMax = 0.1f;
            }
        }
    }
}
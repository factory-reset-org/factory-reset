using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Tests.EditMode
{
    public class VisionQueryTests
    {
        const float Range = 10f;
        const float HalfAngle = 45f;
        static readonly Vector3 Eye = Vector3.zero;
        static readonly Vector3 Forward = Vector3.forward;

        [Test]
        public void TargetInRangeInConeAndClearIsVisible()
        {
            Vector3 target = new Vector3(0f, 0f, 5f);

            Assert.IsTrue(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void BlockedLineOfSightIsNeverVisibleEvenInRangeAndCone()
        {
            Vector3 target = new Vector3(0f, 0f, 5f);

            Assert.IsFalse(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: false));
        }

        [Test]
        public void TargetBeyondRangeIsNotVisible()
        {
            Vector3 target = new Vector3(0f, 0f, Range + 0.01f);

            Assert.IsFalse(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void TargetExactlyAtRangeIsVisible()
        {
            Vector3 target = new Vector3(0f, 0f, Range);

            Assert.IsTrue(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void TargetOutsideViewConeIsNotVisible()
        {
            // 90 degrees off forward, well outside a 45 degree half-angle.
            Vector3 target = new Vector3(5f, 0f, 0f);

            Assert.IsFalse(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void TargetExactlyOnConeEdgeIsVisible()
        {
            float radians = HalfAngle * Mathf.Deg2Rad;
            Vector3 target = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * 5f;

            Assert.IsTrue(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void TargetJustPastConeEdgeIsNotVisible()
        {
            float radians = (HalfAngle + 1f) * Mathf.Deg2Rad;
            Vector3 target = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * 5f;

            Assert.IsFalse(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void TargetDirectlyBehindIsNotVisible()
        {
            Vector3 target = new Vector3(0f, 0f, -5f);

            Assert.IsFalse(VisionQuery.CanSee(Eye, Forward, target, Range, HalfAngle, lineOfSightClear: true));
        }

        [Test]
        public void CoincidentEyeAndTargetIsVisibleWhenClear()
        {
            Assert.IsTrue(VisionQuery.CanSee(Eye, Forward, Eye, Range, HalfAngle, lineOfSightClear: true));
        }
    }
}

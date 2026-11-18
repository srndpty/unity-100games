using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FishingView : MonoBehaviour
    {
        private const float LeaderLength = .2f;
        [SerializeField] private SpriteRenderer _fish;
        [SerializeField] private Sprite[] _fishSprites;
        [SerializeField] private Transform _angler;
        [SerializeField] private Transform _anglerRig;
        [SerializeField] private Sprite _standingSprite;
        [SerializeField] private Sprite _struggleSprite;
        [SerializeField] private Transform _rodHand;
        [SerializeField] private Transform _rodTip;
        [SerializeField] private Transform _struggleHand;
        [SerializeField] private Transform _struggleTip;
        [SerializeField] private float _struggleHorizontal = .065f;
        [SerializeField] private float _struggleVertical = .018f;
        [SerializeField] private float _struggleDegrees = 1.2f;
        [SerializeField] private float _struggleFrequency = 3.2f;
        [SerializeField] private SpriteRenderer _float;
        [SerializeField] private SpriteRenderer _bait;
        [SerializeField] private LineRenderer _rod;
        [SerializeField] private LineRenderer _line;
        [SerializeField] private LineRenderer _underLine;
        [SerializeField] private LineRenderer _ripple;
        [SerializeField] private float _surfaceY = 1.9f;
        [SerializeField] private float _shoreX = -8;
        public Vector2 FishMouth => _fish.transform.position;
        public Vector2 BaitPosition => _bait.transform.position;
        public Transform Angler => _angler;
        public Transform AnglerRig => _anglerRig;
        public Vector3 GripPosition { get; private set; }
        public Vector3 RodTipPosition { get; private set; }
        public LineRenderer FishingLine => _line;
        public Sprite AnglerSprite => _angler.GetComponent<SpriteRenderer>().sprite;
        public bool UnderwaterLineVisible => _underLine.enabled;
        public float SurfaceY => _surfaceY;
        public void Render(FishingState game, FishKind selected, FishingConfig config)
        {
            var kind = game?.Kind ?? selected;
            var profile = Array.Find(config.Fish, fish => fish.Kind == kind);
            var tick = (float)(game?.TickCount ?? 0);
            var phaseTick = game?.PhaseTick ?? 0;
            var phase = game?.Phase ?? FishingPhase.Roaming;
            var struggling = phase == FishingPhase.Fighting || phase == FishingPhase.Landing;
            var strength = phase == FishingPhase.Fighting ? (.4f + (float)game.Tension * .006f) * (game.Action == FishAction.Run ? 1 : .45f) : 0;
            var sway = Mathf.Sin(phaseTick * Mathf.PI * 2 * _struggleFrequency / FishingConfig.TickRate) * strength;
            _anglerRig.localPosition = new Vector3(sway * _struggleHorizontal, Mathf.Abs(sway) * _struggleVertical, 0);
            _anglerRig.localRotation = Quaternion.Euler(0, 0, sway * _struggleDegrees);
            _angler.GetComponent<SpriteRenderer>().sprite = struggling ? _struggleSprite : _standingSprite;
            GripPosition = (struggling ? _struggleHand : _rodHand).position;
            RodTipPosition = (struggling ? _struggleTip : _rodTip).position;
            var targetX = _shoreX + profile.Distance * .48f;
            var home = new Vector2(targetX + 1, _surfaceY - 2.3f - (int)kind * .8f);
            var target = new Vector2(targetX, _surfaceY + .04f - LeaderLength);
            var patrol = Patrol(home, tick);
            var mouth = patrol;
            var angle = 0f;
            var faceRight = Mathf.Cos(tick * .035f) > 0;
            if (phase == FishingPhase.Approaching)
            {
                var duration = FishingConfig.Ticks(config.ApproachSeconds);
                var from = Patrol(home, tick - phaseTick);
                var t = Mathf.SmoothStep(0, 1, phaseTick / (float)duration);
                mouth = Vector2.Lerp(from, target, t); faceRight = target.x > from.x;
                angle = Mathf.Atan2(target.y - from.y, Mathf.Abs(target.x - from.x)) * Mathf.Rad2Deg;
                if (!faceRight) { angle = -angle; }
            }
            else if (phase == FishingPhase.Biting)
            { mouth = target; faceRight = false; angle = -18; }
            else if (phase == FishingPhase.Fighting)
            {
                var dive = Mathf.SmoothStep(0, 1, phaseTick / 45f);
                var depth = _surfaceY - 2.1f - (int)kind * .6f;
                mouth = new Vector2(_shoreX + (float)game.Distance * .48f, Mathf.Lerp(target.y, depth, dive));
                mouth.y += Mathf.Sin(tick * (game.Action == FishAction.Run ? .18f : .045f)) * .2f * dive;
                faceRight = false; angle = Mathf.Lerp(-18, 0, dive);
            }
            else if (phase == FishingPhase.Landing || phase == FishingPhase.Caught)
            {
                var duration = FishingConfig.Ticks(config.LandingSeconds);
                var t = phase == FishingPhase.Caught ? 1 : Mathf.SmoothStep(0, 1, phaseTick / (float)duration);
                mouth = Vector2.Lerp(new Vector2(_shoreX + (float)(game?.Distance ?? 1) * .48f, _surfaceY - 2.1f - (int)kind * .6f), new Vector2(_shoreX - .8f, _surfaceY + .9f), t);
                faceRight = false; angle = -55 * t;
            }
            _fish.sprite = _fishSprites[(int)kind];
            var width = kind == FishKind.Crucian ? 1.8f : kind == FishKind.Bass ? 2.3f : 2.5f;
            var scale = width / _fish.sprite.bounds.size.x;
            _fish.transform.localScale = new Vector3(faceRight ? -scale : scale, scale, 1);
            _fish.transform.position = mouth;
            _fish.transform.rotation = Quaternion.Euler(0, 0, angle);
            _fish.enabled = phase != FishingPhase.Escaped;

            var floating = new Vector2(targetX, _surfaceY + .04f);
            if (phase == FishingPhase.Casting)
            {
                var t = phaseTick / (float)FishingConfig.Ticks(config.CastSeconds);
                floating = Vector2.Lerp(GripPosition, floating, t) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * 2);
            }
            var attached = phase == FishingPhase.Biting || phase == FishingPhase.Fighting || phase == FishingPhase.Landing || phase == FishingPhase.Caught;
            var bait = attached ? mouth : new Vector2(floating.x, floating.y - LeaderLength);
            var tip = (Vector2)RodTipPosition;
            var hand = (Vector2)GripPosition;
            var hooked = phase == FishingPhase.Fighting || phase == FishingPhase.Landing || phase == FishingPhase.Caught;
            var lineDirection = (tip - bait).normalized;
            if (hooked) { floating = bait + lineDirection * LeaderLength; }
            _float.transform.position = floating; _bait.transform.position = bait;
            _float.transform.rotation = Quaternion.Euler(0, 0, hooked ? Mathf.Atan2(lineDirection.y, lineDirection.x) * Mathf.Rad2Deg - 90 : 0);
            _bait.enabled = phase != FishingPhase.Escaped;
            _float.enabled = phase != FishingPhase.Landing && phase != FishingPhase.Caught;
            var bend = phase == FishingPhase.Fighting ? (float)game.Tension / 100 : 0;
            _rod.positionCount = 9;
            for (var i = 0; i < 9; i++)
            {
                var t = i / 8f; var point = Vector2.Lerp(hand, tip, t);
                point.y += Mathf.Sin(t * Mathf.PI) * (.15f + bend * .35f);
                _rod.SetPosition(i, new Vector3(point.x, point.y, 0));
            }
            Segment(_line, tip, hooked ? bait : floating);
            _underLine.enabled = !hooked && attached && _float.enabled;
            Segment(_underLine, floating, bait);
            _ripple.enabled = phase != FishingPhase.Casting && phase != FishingPhase.Landing && phase != FishingPhase.Caught
                && (!hooked || Mathf.Abs(floating.y - _surfaceY) < .1f);
            _ripple.positionCount = 25;
            var radius = phase == FishingPhase.Biting ? .65f + Mathf.Sin(phaseTick * .3f) * .1f : .3f;
            for (var i = 0; i < 25; i++)
            {
                var theta = i / 24f * Mathf.PI * 2;
                _ripple.SetPosition(i, new Vector3(floating.x + Mathf.Cos(theta) * radius, _surfaceY + Mathf.Sin(theta) * .08f, 0));
            }
        }
        private static Vector2 Patrol(Vector2 home, float tick)
            => home + new Vector2(Mathf.Sin(tick * .035f) * 1.4f, Mathf.Sin(tick * .057f) * .45f);
        private static void Segment(LineRenderer line, Vector2 from, Vector2 to)
        { line.positionCount = 2; line.SetPosition(0, new Vector3(from.x, from.y, 0)); line.SetPosition(1, new Vector3(to.x, to.y, 0)); }
    }
}

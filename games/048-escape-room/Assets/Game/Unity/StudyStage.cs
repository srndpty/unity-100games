#nullable enable
using System;
using Game.App;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 書斎の描画。部品ごとの物（名前は Core の部品の ID、位置と大きさは Core の箱、照合専用の BoxCollider 付き）を
    /// その Tick の状態で出し入れし、桁・リング・針・宝石・はめた物・手に取った物・手がかり・望遠鏡の星を写す（補間しない）。
    /// 配置の正は Stage Prefab で、生成スクリプトとテストが Core の宣言と一致するかを <see cref="Mismatch"/> で確かめる。
    /// </summary>
    public sealed class StudyStage : MonoBehaviour
    {
        /// <summary>望遠鏡の星が 1 つ光る Tick と、1 巡の後の休み。</summary>
        public const int StarOnTicks = 30;
        public const int StarStepTicks = 45;
        public const int StarRestTicks = 90;

        /// <summary>手に取った物をカメラの前に置く距離（m）。</summary>
        public const float HeldDistance = 0.55f;

        [SerializeField] private Transform[] _parts = Array.Empty<Transform>();
        [SerializeField] private GameObject?[] _installed = Array.Empty<GameObject?>();
        [SerializeField] private Renderer[] _dialDigits = Array.Empty<Renderer>();
        [SerializeField] private Transform[] _rings = Array.Empty<Transform>();
        [SerializeField] private Renderer[] _ringSymbols = Array.Empty<Renderer>();
        [SerializeField] private Transform? _hourHand;
        [SerializeField] private Transform? _minuteHand;
        [SerializeField] private Renderer[] _gems = Array.Empty<Renderer>();
        [SerializeField] private GameObject[] _stars = Array.Empty<GameObject>();
        [SerializeField] private GameObject? _sky;
        [SerializeField] private GameObject[] _heldModels = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _clues = Array.Empty<GameObject>();
        [SerializeField] private Renderer[] _cylinderDigits = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] _discSymbols = Array.Empty<Renderer>();
        [SerializeField] private Transform? _bookHour;
        [SerializeField] private Transform? _bookMinute;
        [SerializeField] private Transform? _doorHinge;
        [SerializeField] private Material[] _digitMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] _symbolMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] _gemMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] _gemLitMaterials = Array.Empty<Material>();
        [SerializeField] private Sprite[] _icons = Array.Empty<Sprite>();

        private int _mechanismDial = -1;
        private int _mechanismGlobe = -1;
        private int _mechanismClock = -1;
        private int _mechanismSafe = -1;
        private int _telescopeView = -1;

        public Transform PartAt(int index) => _parts[index];

        public int PartCount => _parts.Length;

        public GameObject HeldModel(int examinable) => _heldModels[examinable];

        public GameObject Clue(int examinable) => _clues[examinable];

        public GameObject Star(int gem) => _stars[gem];

        public Renderer Gem(int gem) => _gems[gem];

        /// <summary>手がかりの描画（テストが答えと照合する）：筒の底の桁・円盤の裏の星座・天文書の針。</summary>
        public Renderer CylinderDigit(int i) => _cylinderDigits[i];

        public Renderer DiscSymbol(int i) => _discSymbols[i];

        public Transform BookHour => _bookHour!;

        public Transform BookMinute => _bookMinute!;

        /// <summary>持ち物の絵（ID の順は Core の持ち物の番号）。</summary>
        public Sprite? Icon(int item) => item >= 0 && item < _icons.Length ? _icons[item] : null;

        /// <summary>
        /// Prefab が Core の部屋の宣言と一致するか。部品ごとに名前（ID）・位置・大きさ・BoxCollider を比べ、仕掛けごとの描画の部品の数も確かめる。
        /// 一致すれば null、違えば理由。
        /// </summary>
        public string? Mismatch(RoomDef def)
        {
            if (def == null) { throw new ArgumentNullException(nameof(def)); }
            if (_parts.Length != def.Parts.Count) { return $"部品の数が {_parts.Length} です（Core は {def.Parts.Count}）"; }
            for (int i = 0; i < _parts.Length; i++)
            {
                var part = def.Parts[i];
                var t = _parts[i];
                if (t == null) { return $"部品 {part.Id} がありません"; }
                if (t.name != part.Id) { return $"部品 {i} の名前が {t.name} です（Core は {part.Id}）"; }
                if (t.parent != transform && t.parent?.parent != transform) { return $"部品 {part.Id} が Stage の直下にありません"; }
                if ((t.position - transform.TransformPoint(StageMath.Center(part.Box))).sqrMagnitude > 1e-8f) { return $"部品 {part.Id} の位置が Core の箱と違います"; }
                if ((t.lossyScale - StageMath.Size(part.Box)).sqrMagnitude > 1e-8f) { return $"部品 {part.Id} の大きさが Core の箱と違います"; }
                if (t.rotation != transform.rotation) { return $"部品 {part.Id} が回っています"; }
                var collider = t.GetComponent<BoxCollider>();
                if (collider == null || collider.center != Vector3.zero || collider.size != Vector3.one) { return $"部品 {part.Id} の BoxCollider が箱と合いません"; }
            }

            if (_installed.Length != def.Mechanisms.Count) { return "はめた物の数が仕掛けの数と違います"; }
            if (_dialDigits.Length != DialLock.Digits || _rings.Length != RingLock.RingCount || _ringSymbols.Length != RingLock.RingCount) { return "桁・リングの数が違います"; }
            if (_gems.Length != SequenceLock.GemCount || _stars.Length != SequenceLock.GemCount) { return "宝石・星の数が違います"; }
            if (_heldModels.Length != def.Examinables.Count || _clues.Length != def.Examinables.Count) { return "手に取る物の数が違います"; }
            if (_icons.Length != def.Items.Count) { return "持ち物の絵の数が違います"; }
            if (_digitMaterials.Length != 10 || _symbolMaterials.Length != RingLock.Constellations.Count) { return "数字・星座のマテリアルの数が違います"; }
            return null;
        }

        /// <summary>その Tick の Flow の状態で置く。</summary>
        public void Show(EscapeFlow flow, Camera camera)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (camera == null) { throw new ArgumentNullException(nameof(camera)); }
            var def = flow.Def;
            if (_mechanismDial < 0) { Resolve(def); }
            for (int i = 0; i < _parts.Length; i++) { SetActive(_parts[i].gameObject, flow.IsPresent(i)); }
            for (int m = 0; m < _installed.Length; m++)
            {
                var installed = _installed[m];
                if (installed != null) { SetActive(installed, flow.PhaseOf(m) != Phase.Locked); }
            }

            var answers = flow.Room.Mechanisms;
            for (int i = 0; i < _dialDigits.Length; i++) { SetMaterial(_dialDigits[i], _digitMaterials[flow.ValueOf(_mechanismDial, i)]); }
            for (int i = 0; i < _rings.Length; i++)
            {
                var v = flow.ValueOf(_mechanismGlobe, i);
                _rings[i].localRotation = Quaternion.Euler(0f, v * 45f, 0f);
                SetMaterial(_ringSymbols[i], _symbolMaterials[v]);
            }

            var hour = flow.ValueOf(_mechanismClock, 0);
            var minute = flow.ValueOf(_mechanismClock, 1);
            if (_hourHand != null) { _hourHand.localRotation = Quaternion.Euler(0f, 0f, -((hour * 30f) + (minute * 2.5f))); }
            if (_minuteHand != null) { _minuteHand.localRotation = Quaternion.Euler(0f, 0f, -(minute * 30f)); }

            // 金庫の宝石：正しく押した数だけ、答えの順に光る。
            var safeAnswer = answers[_mechanismSafe].Answer;
            var pressed = flow.PhaseOf(_mechanismSafe) == Phase.Solved ? SequenceLock.GemCount : flow.ValueOf(_mechanismSafe, 0);
            for (int g = 0; g < _gems.Length; g++)
            {
                var lit = false;
                for (int k = 0; k < pressed; k++) { lit |= safeAnswer[k] == g; }
                SetMaterial(_gems[g], lit ? _gemLitMaterials[g] : _gemMaterials[g]);
            }

            // 望遠鏡をのぞいている間、窓の外の星が答えの順に瞬く（Tick で進めるので撮影でも同じ絵）。
            var looking = flow.InGame && flow.View == _telescopeView && !flow.InTransition && flow.Held < 0;
            var cycle = (SequenceLock.GemCount * StarStepTicks) + StarRestTicks;
            var phase = (int)(flow.PlayTicks % cycle);
            if (_sky != null) { SetActive(_sky, looking); }
            for (int g = 0; g < _stars.Length; g++)
            {
                var order = IndexOf(safeAnswer, g);
                var start = order * StarStepTicks;
                SetActive(_stars[g], looking && phase >= start && phase < start + StarOnTicks);
            }

            ShowClues(flow, answers);
            ShowHeld(flow, camera);
            if (_doorHinge != null) { _doorHinge.localRotation = Quaternion.Euler(0f, flow.Escaped ? 80f : 0f, 0f); }
        }

        private void ShowClues(EscapeFlow flow, System.Collections.ObjectModel.ReadOnlyCollection<IMechanism> answers)
        {
            var dial = answers[_mechanismDial].Answer;
            for (int i = 0; i < _cylinderDigits.Length; i++) { SetMaterial(_cylinderDigits[i], _digitMaterials[dial[i]]); }
            var rings = answers[_mechanismGlobe].Answer;
            for (int i = 0; i < _discSymbols.Length; i++) { SetMaterial(_discSymbols[i], _symbolMaterials[rings[i]]); }
            var time = answers[_mechanismClock].Answer;
            if (_bookHour != null) { _bookHour.localRotation = Quaternion.Euler(0f, 0f, -((time[0] * 30f) + (time[1] * 2.5f))); }
            if (_bookMinute != null) { _bookMinute.localRotation = Quaternion.Euler(0f, 0f, -(time[1] * 30f)); }
        }

        private void ShowHeld(EscapeFlow flow, Camera camera)
        {
            var held = flow.InGame ? flow.Held : -1;
            for (int e = 0; e < _heldModels.Length; e++)
            {
                SetActive(_heldModels[e], e == held);
                SetActive(_clues[e], e == held && flow.ClueFacing);
            }

            if (held < 0) { return; }

            // Core の向き：物の座標をヨー（y 軸）→ ピッチ（x 軸）の順に回して、カメラと同じ向きの空間へ写す。
            var o = flow.Orientation;
            var local = Quaternion.AngleAxis(o.Pitch * 90f, Vector3.right) * Quaternion.AngleAxis(o.Yaw * 45f, Vector3.up);
            var cam = camera.transform;
            _heldModels[held].transform.SetPositionAndRotation(cam.position + (cam.forward * HeldDistance), cam.rotation * local);
        }

        private void Resolve(RoomDef def)
        {
            foreach (var m in def.Mechanisms)
            {
                switch (m.Kind)
                {
                    case MechanismKind.Dial:
                        _mechanismDial = m.Index;
                        break;
                    case MechanismKind.Rings:
                        _mechanismGlobe = m.Index;
                        break;
                    case MechanismKind.Clock:
                        _mechanismClock = m.Index;
                        break;
                    case MechanismKind.Sequence:
                        _mechanismSafe = m.Index;
                        break;
                    default:
                        break;
                }
            }

            foreach (var v in def.Views)
            {
                if (v.Reveals >= 0 && def.Mechanisms[v.Reveals].Kind == MechanismKind.Sequence) { _telescopeView = v.Index; }
            }
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) { return i; }
            }

            return -1;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) { go.SetActive(active); }
        }

        private static void SetMaterial(Renderer r, Material m)
        {
            if (r.sharedMaterial != m) { r.sharedMaterial = m; }
        }
    }
}

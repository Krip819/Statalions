using System;
using System.Collections.Generic;
using UnityEngine;

namespace Stalions.Prototype
{
    public enum BeatActionType
    {
        Rest,
        Ppsh,
        Rifle,
        Grenade,
        Smoke,
        Heal,
        Accent,
        Echo,
        Dash
    }

    public enum PrototypePhase
    {
        FrontMap,
        Mission,
        Extraction,
        Departing,
        Debrief
    }

    public enum RunUpgradeType
    {
        Damage,
        Tempo,
        Vitality,
        GrenadeRadius,
        AddHeal,
        AddAccent,
        AddEcho,
        DashSlash
    }

    [Serializable]
    public sealed class BeatSequenceModel
    {
        public const int SlotCount = 8;

        [SerializeField]
        private BeatActionType[] slots;

        public BeatSequenceModel()
        {
            slots = new[]
            {
                BeatActionType.Ppsh,
                BeatActionType.Rest,
                BeatActionType.Rifle,
                BeatActionType.Rest,
                BeatActionType.Grenade,
                BeatActionType.Rest,
                BeatActionType.Ppsh,
                BeatActionType.Rest
            };
        }

        public BeatSequenceModel(BeatSequenceModel source)
        {
            slots = new BeatActionType[SlotCount];
            Array.Copy(source.slots, slots, SlotCount);
        }

        public BeatActionType this[int index]
        {
            get => slots[index];
            set => slots[index] = value;
        }

        public IReadOnlyList<BeatActionType> Slots => slots;

        public void Swap(int first, int second)
        {
            if (first < 0 || first >= SlotCount || second < 0 || second >= SlotCount)
            {
                return;
            }

            (slots[first], slots[second]) = (slots[second], slots[first]);
        }

        public bool TryReplaceFirstRest(BeatActionType action)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != BeatActionType.Rest)
                {
                    continue;
                }

                slots[i] = action;
                return true;
            }

            return false;
        }

        public bool HasRest()
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == BeatActionType.Rest)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Contains(BeatActionType action)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == action)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class SectorState
    {
        private const string SavePrefix = "Stalions.Prototype.Sector.";

        public SectorState(string id, string displayName, float initialSovietControl, float danger)
        {
            Id = id;
            DisplayName = displayName;
            InitialSovietControl = initialSovietControl;
            Danger = danger;
            SovietControl = PlayerPrefs.GetFloat(SavePrefix + id, initialSovietControl);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public float InitialSovietControl { get; }
        public float Danger { get; }
        public float SovietControl { get; private set; }
        public float GermanControl => 100f - SovietControl;

        public void AddSovietControl(float amount)
        {
            SovietControl = Mathf.Clamp(SovietControl + amount, 0f, 100f);
            PlayerPrefs.SetFloat(SavePrefix + Id, SovietControl);
            PlayerPrefs.Save();
        }

        public void Reset()
        {
            SovietControl = InitialSovietControl;
            PlayerPrefs.DeleteKey(SavePrefix + Id);
        }
    }

    public static class BeatActionNames
    {
        public static string Short(BeatActionType action)
        {
            return action switch
            {
                BeatActionType.Ppsh => "ППШ",
                BeatActionType.Rifle => "ВИН",
                BeatActionType.Grenade => "ГРН",
                BeatActionType.Smoke => "ДЫМ",
                BeatActionType.Heal => "МЕД",
                BeatActionType.Accent => "АКЦ",
                BeatActionType.Echo => "ЭХО",
                BeatActionType.Dash => "РЫВ",
                _ => "—"
            };
        }

        public static string Long(BeatActionType action)
        {
            return action switch
            {
                BeatActionType.Ppsh => "ППШ: очередь",
                BeatActionType.Rifle => "Винтовка: точный выстрел",
                BeatActionType.Grenade => "Осколочная граната",
                BeatActionType.Smoke => "Дымовая завеса",
                BeatActionType.Heal => "Перевязка",
                BeatActionType.Accent => "Акцент: усилить следующее оружие",
                BeatActionType.Echo => "Эхо: повторить предыдущее оружие",
                BeatActionType.Dash => "Рывок вперёд",
                _ => "Передышка"
            };
        }

        public static string Pattern(BeatActionType action)
        {
            return action switch
            {
                BeatActionType.Ppsh => "КОНУС",
                BeatActionType.Rifle => "ЛИНИЯ",
                BeatActionType.Grenade => "КРУГ",
                BeatActionType.Smoke => "КРУГ",
                BeatActionType.Heal => "СЕБЯ",
                BeatActionType.Accent => "УСИЛ.",
                BeatActionType.Echo => "ПОВТОР",
                BeatActionType.Dash => "ПРОРЫВ",
                _ => "ПАУЗА"
            };
        }
    }

    public static class FactionContributionCalculator
    {
        public static float Calculate(bool objectiveComplete, bool extracted)
        {
            if (!objectiveComplete)
            {
                return 0f;
            }

            return extracted ? 5f : 2f;
        }
    }

    public static class CombatBoundaryGeometry
    {
        public static bool Contains(
            Vector3 position,
            float halfWidth,
            float halfHeight,
            float inset = 0f)
        {
            var usableHalfWidth = Mathf.Max(0f, halfWidth - Mathf.Max(0f, inset));
            var usableHalfHeight = Mathf.Max(0f, halfHeight - Mathf.Max(0f, inset));
            return Mathf.Abs(position.x) <= usableHalfWidth &&
                   Mathf.Abs(position.z) <= usableHalfHeight;
        }

        public static Vector3 ClosestPointInside(
            Vector3 position,
            float halfWidth,
            float halfHeight,
            float inset = 0f)
        {
            var usableHalfWidth = Mathf.Max(0f, halfWidth - Mathf.Max(0f, inset));
            var usableHalfHeight = Mathf.Max(0f, halfHeight - Mathf.Max(0f, inset));
            return new Vector3(
                Mathf.Clamp(position.x, -usableHalfWidth, usableHalfWidth),
                0f,
                Mathf.Clamp(position.z, -usableHalfHeight, usableHalfHeight));
        }

        public static float DistanceOutside(
            Vector3 position,
            float halfWidth,
            float halfHeight)
        {
            var closest = ClosestPointInside(position, halfWidth, halfHeight);
            var offset = position - closest;
            offset.y = 0f;
            return offset.magnitude;
        }
    }

    [Serializable]
    public sealed class CombatBoundaryTimer
    {
        public CombatBoundaryTimer(float duration)
        {
            Duration = Mathf.Max(0.01f, duration);
            Reset();
        }

        public float Duration { get; }
        public float Remaining { get; private set; }
        public bool Active { get; private set; }
        public bool Expired { get; private set; }

        public bool Tick(
            bool outsideBoundary,
            bool safelyInsideBoundary,
            float deltaTime)
        {
            if (Expired)
            {
                return true;
            }

            if (!Active && outsideBoundary)
            {
                Active = true;
            }

            if (!Active)
            {
                return false;
            }

            if (safelyInsideBoundary)
            {
                Reset();
                return false;
            }

            Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, deltaTime));
            Expired = Remaining <= 0f;
            return Expired;
        }

        public void Reset()
        {
            Remaining = Duration;
            Active = false;
            Expired = false;
        }
    }
}

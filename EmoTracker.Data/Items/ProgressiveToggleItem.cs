using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("progressive_toggle")]
    public class ProgressiveToggleItem : ItemBase
    {
        public bool SwapActions = false;
        public uint StageCount = 0;

        public bool Active
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                SetTransactableProperty(value, (processedValue) =>
                {
                    UpdateImage();
                });
            }
        }

        public uint CurrentStage
        {
            get { return GetTransactableProperty<uint>(); }
            set
            {
                SetTransactableProperty(value, (processedValue) =>
                {
                    UpdateImage();
                });
            }
        }

        Dictionary<KeyValuePair<bool, uint>, Stage> mStages = new Dictionary<KeyValuePair<bool, uint>, Stage>();

        public class Stage : CodeProvider
        {
            CodeProvider mPrivateCodes = new CodeProvider();

            public ImageReference Icon { get; set; }
            public CodeProvider PrivateCodes { get { return mPrivateCodes; } }
        }

        public Stage GetActiveStageForIndex(uint index)
        {
            Stage stageDef;
            if (mStages.TryGetValue(new KeyValuePair<bool, uint>(true, index), out stageDef))
                return stageDef;

            return null;
        }

        public void AddStage(IGamePackage package, uint stage, Stage stageDef, ImageReference disabledImage = null)
        {
            StageCount = Math.Max(StageCount, stage + 1);

            disabledImage = disabledImage ?? ImageReference.FromImageReference(stageDef.Icon, DisabledImageFilterSpec);

            mStages[new KeyValuePair<bool, uint>(false, stage)] = new Stage() { Icon = disabledImage };
            mStages[new KeyValuePair<bool, uint>(true, stage)] = stageDef;

            UpdateImage();
        }

        public override bool CanProvideCode(string code)
        {
            foreach (Stage stage in mStages.Values)
            {
                if (stage != null && stage.ProvidesCode(code))
                    return true;
            }

            return false;
        }

        public override IEnumerable<string> GetAllProvidedCodes()
        {
            var codes = new HashSet<string>();
            foreach (Stage stage in mStages.Values)
            {
                if (stage != null)
                {
                    foreach (string code in stage.ProvidedCodes)
                        codes.Add(code);
                }
            }
            return codes;
        }

        public override uint ProvidesCode(string code)
        {
            Stage stageDef;
            if (mStages.TryGetValue(new KeyValuePair<bool, uint>(Active, CurrentStage), out stageDef))
            {
                if (stageDef.ProvidesCode(code))
                    return 1;
            }

            return 0;
        }
        public override void AdvanceToCode(string code = null)
        {
            Active = true;
            UpdateImage();
        }

        public void Advance()
        {
            CurrentStage = (CurrentStage + 1) % StageCount;
            UpdateImage();
        }

        public void AdvanceToPrivateCode(string code = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            if (StageCount > 0)
            {
                int currStage = (int)CurrentStage;

                for (int fwd = currStage + 1; fwd < StageCount; ++fwd)
                {
                    Stage candidate = mStages[new KeyValuePair<bool, uint>(true, (uint)fwd)];
                    if (candidate.PrivateCodes.ProvidesCode(code) || candidate.ProvidesCode(code))
                    {
                        CurrentStage = (uint)fwd;
                        UpdateImage();
                        return;
                    }
                }

                for (int bkwd = currStage - 1; bkwd >= 0; --bkwd)
                {
                    Stage candidate = mStages[new KeyValuePair<bool, uint>(true, (uint)bkwd)];
                    if (candidate.PrivateCodes.ProvidesCode(code) || candidate.ProvidesCode(code))
                    {
                        CurrentStage = (uint)bkwd;
                        UpdateImage();
                        return;
                    }
                }
            }
        }

        public override void OnLeftClick()
        {
            if (!SwapActions)
                Active = !Active;
            else
                Advance();
        }

        public override void OnRightClick()
        {
            if (SwapActions)
                Active = !Active;
            else
                Advance();
        }
        protected void UpdateImage()
        {
            Stage stageDef;
            if (mStages.TryGetValue(new KeyValuePair<bool, uint>(Active, CurrentStage), out stageDef))
            {
                Icon = stageDef.Icon;
            }

            Stage activeStageDef;
            if (mStages.TryGetValue(new KeyValuePair<bool, uint>(true, CurrentStage), out activeStageDef))
            {
                PotentialIcon = activeStageDef.Icon;
            }
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            SwapActions = data.GetValue<bool>("swap_actions", false);

            uint stageIdx = 0;

            JArray stages = (JArray)data.GetValue("stages");
            foreach (JObject stageData in stages)
            {
                var img = ImageReference.FromPackRelativePath(package, stageData.GetValue<string>("img"), stageData.GetValue<string>("img_mods"));
                var disabledImg = ImageReference.FromPackRelativePath(package, stageData.GetValue<string>("disabled_img"), stageData.GetValue<string>("disabled_img_mods") ?? DisabledImageFilterSpec);

                if (img != null)
                {
                    Data.Items.ProgressiveToggleItem.Stage stage = new Data.Items.ProgressiveToggleItem.Stage();
                    stage.Icon = img;

                    string codes = stageData.GetValue<string>("codes");
                    if (!string.IsNullOrWhiteSpace(codes))
                        stage.AddCodes(codes);

                    string privateCodes = stageData.GetValue<string>("secondary_codes");
                    if (!string.IsNullOrWhiteSpace(privateCodes))
                        stage.PrivateCodes.AddCodes(privateCodes);

                    AddStage(package, stageIdx++, stage, disabledImg);
                }
            }

            if (StageCount > 0)
            {
                CurrentStage = data.GetValue<uint>("initial_stage_idx", CurrentStage) % StageCount;
                Active = data.GetValue<bool>("initial_active_state", Active);
            }
        }

        protected override bool Save(JObject data)
        {
            data["stage_index"] = CurrentStage;
            data["active"] = Active;

            return true;
        }

        protected override bool Load(JObject data)
        {
            int stageIdx = data.GetValue<int>("stage_index", -1);
            int stageCount = mStages.Count;

            if (stageIdx < 0 || stageIdx > (StageCount - 1))
                return false;

            CurrentStage = (uint)stageIdx;
            Active = data.GetValue<bool>("active", Active);

            UpdateImage();

            return true;
        }
    }
}

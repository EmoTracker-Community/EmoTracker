using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("progressive")]
    public class ProgressiveItem : ItemBase
    {
        public class Stage : CodeProvider
        {
            public ImageReference Icon { get; set; }
            public string Name { get; set; }
        }

        protected List<Stage> StagesInternal = new List<Stage>();
        string mDisabledImageMods;
        bool mbLoop = false;

        public IEnumerable<Stage> Stages
        {
            get { return StagesInternal; }
        }

        public bool Loop
        {
            get { return mbLoop; }
            set { SetProperty(ref mbLoop, value); }
        }

        private Stage CurrentStageInstance
        {
            get
            {
                if (StagesInternal.Count > 0)
                    return StagesInternal[CurrentStage];

                return null;
            }
        }

        [DependentProperty("CurrentStageInstance")]
        public int CurrentStage
        {
            get { return GetTransactableProperty<int>(); }
            set
            {
                if (value >= 0 && value < StagesInternal.Count)
                {
                    SetTransactableProperty(value, (processedValue =>
                    {
                        UpdateAppearance();
                    }));
                }
            }
        }

        public void AddStage(Stage stage, bool enableDisabledStage, IGamePackage package)
        {
            //  Add a disabled stage once we get our first stage added
            if (StagesInternal.Count == 0 && enableDisabledStage)
            {
                Stage disabled = new Stage()
                {
                    Icon = ImageReference.FromImageReference(stage.Icon, mDisabledImageMods ?? DisabledImageFilterSpec)
                };
                StagesInternal.Add(disabled);
            }

            StagesInternal.Add(stage);
            UpdateAppearance();
        }

        protected void UpdateAppearance()
        {
            if (CurrentStageInstance != null)
            {
                Icon = CurrentStageInstance.Icon;
            }
            else
            {
                Icon = null;
            }

            UpdatePotentialIcon();
        }

        protected void UpdatePotentialIcon()
        {
            int idx = CurrentStage;
            if (++idx < StagesInternal.Count)
            {
                PotentialIcon = StagesInternal[idx].Icon;
            }
            else
            {
                PotentialIcon = Icon;
            }
        }

        public override bool CanProvideCode(string code)
        {
            foreach (Stage stage in StagesInternal)
            {
                if (stage.ProvidesCode(code))
                    return true;
            }

            return false;
        }

        public override IEnumerable<string> GetAllProvidedCodes()
        {
            var codes = new HashSet<string>();
            foreach (Stage stage in StagesInternal)
            {
                foreach (string code in stage.ProvidedCodes)
                    codes.Add(code);
            }
            return codes;
        }

        public override uint ProvidesCode(string code)
        {
            if (CurrentStageInstance != null)
            {
                if (CurrentStageInstance.ProvidesCode(code))
                    return 1;
            }

            return 0;
        }

        public override void AdvanceToCode(string code = null)
        {
            if (code != null)
            {
                int currIdx = CurrentStage;
                for (int i = currIdx + 1; i < StagesInternal.Count; ++i)
                {
                    Stage stage = StagesInternal[i];

                    if (stage.ProvidesCode(code))
                    {
                        CurrentStage = i;
                        return;
                    }
                }

                if (Loop)
                {
                    for (int i = 0; i < StagesInternal.Count; ++i)
                    {
                        Stage stage = StagesInternal[i];

                        if (stage.ProvidesCode(code))
                        {
                            CurrentStage = i;
                            return;
                        }
                    }
                }
            }

            Advance();
        }

        public void Advance()
        {
            int newIdx = CurrentStage + 1;

            if (Loop)
                newIdx = newIdx % StagesInternal.Count;

            if (newIdx < StagesInternal.Count)
            {
                CurrentStage = newIdx;
            }
        }

        public void Downgrade()
        {
            int newIdx = CurrentStage - 1;

            if (Loop)
                newIdx = (newIdx + StagesInternal.Count) % StagesInternal.Count;

            if (newIdx >= 0)
            {
                CurrentStage = newIdx;
            }
        }

        public override void OnLeftClick()
        {
            Advance();
        }

        public override void OnRightClick()
        {
            Downgrade();
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            Loop = data.GetValue<bool>("loop", false);
            bool enableDisabledStage = data.GetValue<bool>("allow_disabled", true);
            mDisabledImageMods = data.GetValue<string>("disabled_img_mods");

            string codes = "";

            JArray stages = (JArray)data.GetValue("stages");
            foreach (JObject stageData in stages)
            {
                var img = ImageReference.FromPackRelativePath(package, stageData.GetValue<string>("img"), stageData.GetValue<string>("img_mods"));

                //  Reset the code inheritance chain if requested
                bool bInheritCodes = stageData.GetValue<bool>("inherit_codes", true);
                if (!bInheritCodes)
                    codes = "";

                if (img != null)
                {
                    Data.Items.ProgressiveItem.Stage stage = new Data.Items.ProgressiveItem.Stage();
                    stage.Icon = img;

                    string stageCodes = stageData.GetValue<string>("codes");
                    if (!string.IsNullOrWhiteSpace(stageCodes))
                        codes = string.Format("{0},{1}", codes, stageCodes);

                    if (!string.IsNullOrWhiteSpace(codes))
                        stage.AddCodes(codes);

                    AddStage(stage, enableDisabledStage, package);
                }
            }

            int initialStageIdx = data.GetValue<int>("initial_stage_idx", -1);
            if (initialStageIdx >= 0 && StagesInternal.Count > 0)
                CurrentStage = (initialStageIdx + (enableDisabledStage ? 1 : 0)) % StagesInternal.Count;
        }

        protected override bool Save(JObject data)
        {
            data["stage_index"] = CurrentStage;
            return true;
        }

        protected override bool Load(JObject data)
        {
            int stageIdx = data.GetValue<int>("stage_index", -1);
            int stageCount = StagesInternal.Count;

            if (stageCount == 0)
                return true;

            if (stageIdx < 0 || stageIdx > (stageCount - 1))
                return false;

            CurrentStage = stageIdx;
            return true;
        }
    }
}

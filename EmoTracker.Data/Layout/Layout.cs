using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

#pragma warning disable CS0618

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: <see cref="Layout"/> is now a <see cref="ModelTypeBase"/>. Its
    /// owned <see cref="Root"/> subtree is forked element-by-element on
    /// <see cref="Fork"/>; cross-references to a Layout (held by
    /// <c>LayoutReference</c> and <c>ButtonPopup</c>) are
    /// <see cref="ModelReference{Layout}"/>-tracked.
    ///
    /// <para>
    /// Layout itself has no <c>[KVOverridable]</c> properties — it's a thin
    /// owner of the root <see cref="LayoutItem"/>. Its identity comes from its
    /// auto-generated <see cref="ModelTypeBase.DefinitionId"/>.
    /// </para>
    /// </summary>
    public partial class Layout : ModelTypeBase
    {
        // Owned subtree: the root LayoutItem. Held as a private field — owning
        // relationship — and forked explicitly.
        LayoutItem mRoot;

        public LayoutItem Root
        {
            get { return mRoot; }
            private set { SetProperty(ref mRoot, value); }
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mRoot);
            base.Dispose();
        }

        public bool Load(Stream stream, IGamePackage package)
        {
            try
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    return Load(root, package) && Root != null;
                }
            }
            catch (Exception e)
            {
                ((this.OwnerState as Sessions.TrackerState)?.Scripts)?.OutputException(e);
            }

            return false;
        }

        public bool Load(JObject root, IGamePackage package)
        {
            try
            {
                // Children inherit this Layout's OwnerState — set on the
                // Layout instance by its caller before Load is invoked.
                Root = LayoutItem.CreateLayoutItem(root, package, this.OwnerState);
            }
            catch
            {
            }

            return Root != null;
        }

        public void Clear()
        {
            Root = null;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new ArgumentNullException(nameof(destOwnerState));
            var copy = (Layout)System.Activator.CreateInstance(this.GetType());
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            if (this.mRoot != null)
                copy.mRoot = (LayoutItem)this.mRoot.Fork(destOwnerState);
            return copy;
        }
    }
}

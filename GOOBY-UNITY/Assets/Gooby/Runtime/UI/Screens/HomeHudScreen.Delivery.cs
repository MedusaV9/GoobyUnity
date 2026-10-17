using Gooby.Core.City;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    public sealed partial class HomeHudScreen
    {
        /// <summary>
        /// Back home with furniture on the way (delivery_cutscene.gd attach_if_pending): the van comes. While Gooby is on
        /// holiday the order waits for his return.
        /// </summary>
        private void TryDelivery()
        {
            if (_host.Ui.Current != this || Travel.IstWeg(_host.Store) || Lieferung.Stueck(_host.Store.State) <= 0) return;
            Debug.Log("[lieferung] kommt: " + Lieferung.Stueck(_host.Store.State) + " Stück");
            _host.Ui.Push(new DeliveryScreen(_host));
        }
    }
}

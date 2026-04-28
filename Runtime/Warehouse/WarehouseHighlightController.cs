using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    internal sealed class WarehouseHighlightController
    {
        private readonly GameObject _highlightCargo;
        private readonly GameObject _highlightIndicator;
        
        public WarehouseHighlightController(GameObject highlightCargo, GameObject highlightIndicator)
        {
            _highlightCargo = highlightCargo;
            _highlightIndicator = highlightIndicator;
        }

        public bool CanHighlight()
        {
            return _highlightCargo != null || _highlightIndicator != null;
        }

        public bool Locate(Transform warehouseTransform, RuntimeBinData binData, bool setActive)
        {
            if (warehouseTransform == null || binData == null)
            {
                return false;
            }

            Vector3 worldPos = warehouseTransform.TransformPoint(binData.Pos);
            Quaternion worldRot = warehouseTransform.rotation;

            if (_highlightCargo != null)
            {
                Transform highlightCargoTransform = _highlightCargo.transform;
                highlightCargoTransform.position = worldPos;
                highlightCargoTransform.rotation = worldRot;
            }

            if (_highlightIndicator != null)
            {
                Transform highlightIndicatorTransform = _highlightIndicator.transform;
                highlightIndicatorTransform.position = worldPos;
                highlightIndicatorTransform.rotation = worldRot;
            }

            if (setActive)
            {
                if (_highlightCargo != null)
                {
                    _highlightCargo.SetActive(binData.ShowCargo);
                }

                if (_highlightIndicator != null)
                {
                    _highlightIndicator.SetActive(true);
                }
            }

            return true;
        }

        public void Hide()
        {
            if (_highlightCargo != null && _highlightCargo.activeSelf)
            {
                _highlightCargo.SetActive(false);
            }

            if (_highlightIndicator != null && _highlightIndicator.activeSelf)
            {
                _highlightIndicator.SetActive(false);
            }
        }
    }
}

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    using UnityEngine;

    public class RollerChain : Chain
    {
        [SerializeField] private GameObject m_rollerPrefab;

        public override void CreateNodes()
        {
            m_nodes = new ChainNode[Count];
            bool replace = false;
            for (int i = 0; i < Count; i++)
            {
                //Alternate prefab.
                var prefab = NodePrefab;
                if (replace)
                    prefab = m_rollerPrefab;

                //Create node.
                var nodeClone = (GameObject)Instantiate(prefab, NodeRoot);
                TowNodeBaseOnCurve(nodeClone.transform, i * Space);

                //Set node ID.
                m_nodes[i] = nodeClone.GetComponent<ChainNode>();
                m_nodes[i].ID = i;

                //Alternate replace.
                replace = !replace;
            }
        }
    }
}
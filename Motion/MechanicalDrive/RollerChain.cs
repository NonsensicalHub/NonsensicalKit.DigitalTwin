using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class RollerChain : Chain
    {
        [SerializeField] private GameObject m_rollerPrefab;

        public override void CreateNodes()
        {
            m_nodes = new ChainNode[Count];
            bool flag = false;
            for (int i = 0; i < Count; i++)
            {
                //Alternate prefab.
                var prefab = flag ? NodePrefab : m_rollerPrefab;

                //Create node.
                var nodeClone = Instantiate(prefab, NodeRoot);
                TowNodeBaseOnCurve(nodeClone.transform, i * Space);

                //Set node ID.
                m_nodes[i] = nodeClone.GetComponent<ChainNode>();
                m_nodes[i].ID = i;

                //Alternate replace.
                flag = !flag;
            }
        }
    }
}

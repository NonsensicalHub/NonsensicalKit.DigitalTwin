using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class Chain : Mechanism
    {
        [SerializeField] private Transform m_anchorRoot;

        [SerializeField] private Transform m_nodeRoot;

        [SerializeField] private GameObject m_nodePrefab;

        [SerializeField] private int m_count = 50;

        [SerializeField] private float m_space = 0.1f;

        [SerializeField] private bool m_alwaysUpdate;
        [SerializeField] protected ChainNode[] m_nodes;

        public int Count { get => m_count; set => m_count = value; }
        public VectorAnimationCurve Curve { get; private set; }
        public Transform AnchorRoot => m_anchorRoot;
        public Transform NodeRoot => m_nodeRoot;
        public GameObject NodePrefab => m_nodePrefab;
        public float Space => m_space;

        private float _timer;

        protected virtual void Awake()
        {
            CreateCurve();
        }

        /// <summary>
        /// Create the curve base on anchors.
        /// </summary>
        public void CreateCurve()
        {
            Curve = new VectorAnimationCurve();

            Curve.PreWrapMode = Curve.PostWrapMode = WrapMode.Loop;

            //Add frame keys to curve.
            float time = 0;
            for (int i = 0; i < AnchorRoot.childCount - 1; i++)
            {
                Curve.AddKey(time, AnchorRoot.GetChild(i).localPosition);
                time += Vector3.Distance(AnchorRoot.GetChild(i).position, AnchorRoot.GetChild(i + 1).position);
            }

            //Add last key and loop key[the first key].
            Curve.AddKey(time, AnchorRoot.GetChild(AnchorRoot.childCount - 1).localPosition);
            time += Vector3.Distance(AnchorRoot.GetChild(AnchorRoot.childCount - 1).position, AnchorRoot.GetChild(0).position);

            Curve.AddKey(time, AnchorRoot.GetChild(0).localPosition);

            //Smooth curve keys out tangent.
            Curve.SmoothTangents(0);
        }

        public virtual void CreateNodes()
        {
            m_nodes = new ChainNode[Count];
            for (int i = 0; i < Count; i++)
            {
                //Create node.
                var nodeClone = Instantiate(NodePrefab, NodeRoot);
                TowNodeBaseOnCurve(nodeClone.transform, i * Space);

                //Set node ID.
                m_nodes[i] = nodeClone.GetComponent<ChainNode>();
                m_nodes[i].ID = i;
            }
        }

        public override void Drive(float power, DriveType driveType)
        {
            if (driveType == DriveType.Angular)
            {
                Debug.Log("此机械结构不支持角运动");
                return;
            }

            if (m_alwaysUpdate)
            {
                CreateCurve();
                var maxTime = Curve[Curve.Length - 1].Time;
                if (Mathf.Abs(_timer) >= maxTime)
                    _timer -= maxTime;
            }

            _timer += power;
            foreach (var node in m_nodes)
            {
                TowNodeBaseOnCurve(node.transform, node.ID * Space + _timer);
            }
        }

        /// <summary>
        /// Tow node move and rotate base on VectorAnimationCurve.
        /// </summary>
        /// <param name="node">Target node to tow.</param>
        /// <param name="time">Time of current in curve.</param>
        protected void TowNodeBaseOnCurve(Transform node, float time)
        {
            //Calculate position and direction.
            var nodePos = Curve.Evaluate(time);
            var nextNodePos = Curve.Evaluate(time + 0.01f);
            var up = Vector3.Cross(nextNodePos - nodePos, transform.forward);

            //Update position and direction.
            node.localPosition = nodePos;
            node.LookAt(NodeRoot.TransformPoint(nextNodePos), up);
        }
    }
}

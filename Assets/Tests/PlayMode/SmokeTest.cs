using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public class SmokeTest
    {
        [UnityTest]
        public IEnumerator Passes()
        {
            yield return null;
            Assert.Pass();
        }
    }
}

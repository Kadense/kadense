using System.Text.Json.Serialization;

namespace Kadense.Models.Kubernetes.CoreApi
{
    public class V1ClusterTrustBundleProjection : KadenseTemplatedCopiedResource<k8s.Models.V1ClusterTrustBundleProjection>
    { 
        [JsonPropertyName("labelSelector")]
        public V1LabelSelector? LabelSelector { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("optional")]
        public bool? Optional { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("signerName")]
        public string? SignerName { get; set; }

        public override k8s.Models.V1ClusterTrustBundleProjection ToOriginal(Dictionary<string, string> variables)
        {
            return new k8s.Models.V1ClusterTrustBundleProjection(
                labelSelector: this.LabelSelector != null ? this.LabelSelector.ToOriginal(variables) : null,
                name: this.GetValue(this.Name, variables),
                optional: this.Optional,
                path: this.GetValue(this.Path, variables),
                signerName: this.GetValue(this.SignerName, variables)
            );
        }
    }
}
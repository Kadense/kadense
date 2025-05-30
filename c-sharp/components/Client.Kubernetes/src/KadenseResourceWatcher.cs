using Kadense.Logging;

using k8s.Models;

namespace Kadense.Client.Kubernetes
{
    public abstract class KadenseResourceWatcher<T>
        where T : IKubernetesObject<V1ObjectMeta>
    {
        private readonly KadenseLogger<KadenseResourceWatcher<T>> _logger;
        public enum ServiceStatus
        {
            Running,
            Stopped,
            Error
        }

        public ServiceStatus Status { get; protected set; } = ServiceStatus.Stopped;

        protected IKubernetes K8sClient { get; set; }

        protected GenericClient? GenericClient { get; set; }

        public KadenseResourceWatcher()
        {
            _logger = new KadenseLogger<KadenseResourceWatcher<T>>();
            var k8sClientFactory = new KubernetesClientFactory();
            this.K8sClient = k8sClientFactory.CreateClient();
        }
        
        public KadenseResourceWatcher(IKubernetes client)
        {
            _logger = new KadenseLogger<KadenseResourceWatcher<T>>();
            var k8sClientFactory = new KubernetesClientFactory();
            this.K8sClient = client;
        }
        public KadenseResourceWatcher(IKubernetes client, GenericClient genericClient)
        {
            _logger = new KadenseLogger<KadenseResourceWatcher<T>>();
            this.K8sClient = client;
            this.GenericClient = genericClient;
        }
        public virtual Watcher<T> Start()
        {
            _logger.LogInformation("Starting watcher for {ResourceName}", typeof(T).Name);
            this.Status = ServiceStatus.Running;
            var watcher = this.GenericClient!.Watch<T>(onEvent: OnWatchEvent , onError: OnWatchError, onClosed: OnWatchStopped);
            _logger.LogInformation("Started watcher for {ResourceName}", typeof(T).Name);
            return watcher;
        }
        
        public virtual void StartAndWait(CancellationToken cancellationToken = default)
        {
            var watcher = this.Start();
            _logger.LogInformation("Waiting for watcher to stop for {ResourceName}", typeof(T).Name);
            Thread.Sleep(1000);
            while (this.Status == ServiceStatus.Running && watcher.Watching && !cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(100);
            }
            _logger.LogInformation($"Watcher stopped for {typeof(T).Name}: Status: {this.Status}, Watching: {watcher.Watching}, IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
        }

        protected virtual void OnWatchError(Exception ex)
        {
            _logger.LogError(ex, "Error in watcher for {ResourceName}", typeof(T).Name);
            this.Status = ServiceStatus.Error;
            throw ex;
        }

        protected virtual void OnWatchStopped()
        {
            _logger.LogInformation("Watcher stopped for {ResourceName}", typeof(T).Name);
            this.Status = ServiceStatus.Stopped;
        }

        protected virtual void OnWatchEvent(WatchEventType watchEventType, T item)
        {
            _logger.LogInformation("Watch event {EventType} for {ResourceName}", watchEventType, typeof(T).Name);
            switch (watchEventType)
            {
                case WatchEventType.Added:
                    try 
                    {
                        var addedTask = OnAddedAsync(item);
                        addedTask.Wait();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in OnAddedAsync for {ResourceName}", typeof(T).Name);
                    }
                    break;

                case WatchEventType.Modified:
                    try 
                    {
                        var updatedTask = OnUpdatedAsync(item);
                        updatedTask.Wait();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in OnUpdatedAsync for {ResourceName}", typeof(T).Name);
                    }
                    break;

                case WatchEventType.Deleted:
                    try {
                        var deletedTask = OnDeletedAsync(item);
                        deletedTask.Wait();
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, "Error in OnDeletedAsync for {ResourceName}", typeof(T).Name);
                    }
                    break;

                case WatchEventType.Error:
                    OnError();
                    break;

                default:
                    throw new NotImplementedException($"WatchEventType {watchEventType} is not recognised");
            }
        }

        public abstract Task<(T?, k8s.Models.Corev1Event?)> OnAddedAsync(T item);
        public abstract Task<(T?, k8s.Models.Corev1Event?)> OnUpdatedAsync(T item);
        public abstract Task<(T?, k8s.Models.Corev1Event?)> OnDeletedAsync(T item);
        protected virtual void OnError()
        {
            _logger.LogError("Unexpected error in watcher for {ResourceName}", typeof(T).Name);
        }

        protected virtual async Task<Corev1Event> CreateEventAsync(V1ObjectReference involvedObject, string action, string reason, Corev1EventSeries? series = null, V1ObjectReference? related = null, string type = "Normal", string? message = null)
        {
            var body = new k8s.Models.Corev1Event(
                    metadata: new V1ObjectMeta(
                        generateName: $"{involvedObject.Name}.",
                        namespaceProperty: involvedObject.NamespaceProperty
                    ),
                    reportingComponent: System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name,
                    reportingInstance: Environment.MachineName,
                    eventTime: DateTime.UtcNow,
                    action: action,
                    involvedObject: involvedObject,
                    related: related,
                    reason: reason,
                    message: message,
                    type: type
                );

            var evt = await this.K8sClient.CoreV1.CreateNamespacedEventAsync(
                namespaceParameter: involvedObject.NamespaceProperty,
                body: body
            );

            _logger.LogInformation($"Event created: {evt.Metadata.Name} for {involvedObject.Name}: {action} - {reason}");

            return evt;
        }
    }
}
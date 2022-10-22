using Amazon.CDK;

namespace Cdk
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            var regionsToDeploy = new[] { "eu-north-1", "eu-central-1" };

            foreach (var region in regionsToDeploy)
            {
                var stack = new ChatAppStack(app, $"ChatAppStack-{region}", new ChatAppStackProps
                {
                    RegionsToReplicate = regionsToDeploy.Where(x => x != region).ToList(),
                    Env = new Amazon.CDK.Environment
                    {
                        Region = region
                    }
                });
            }

            app.Synth();
        }
    }
}

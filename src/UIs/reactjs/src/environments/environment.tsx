const environment = {
  name: "production",
  OpenIdConnect: {
    Authority: "https://localhost:44368",
    ClientId: "ClassifiedAds.React",
  },
  ResourceServer: {
    Endpoint: "https://localhost:44312/api/",
    NotificationEndpoint: "https://localhost:44312/hubs/notification",
  },
  CurrentUrl: "http://localhost:3000/",
};
export default environment;

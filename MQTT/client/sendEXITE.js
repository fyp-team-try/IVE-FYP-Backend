const mqtt = require("mqtt");
const env = require("dotenv").config();
const client = mqtt.connect(process.env.MQTT_HOST);
client.subscribe("LPR");

client.on("connect", () => {
  client.publish(
    "LPR",
    `{
        "lotID": "1",
        "gateType": "IN_Electronic",
        "vehicleLicense": "ALEXE"
    }`,
    {},
    function(err) {
      if (!err) {
        client.end();
      }
    }
  );
});

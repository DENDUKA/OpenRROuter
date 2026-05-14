const fs = require('fs');

async function test() {
  try {
    const { getAdapter } = await import('../src/lib/db/driver.js');
    const adapter = await getAdapter();
    console.log("Success:", adapter.driver);
  } catch (e) {
    console.error("Error:", e.message);
  }
}
test();

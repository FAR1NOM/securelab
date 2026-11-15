
document.querySelector("#probe").addEventListener("click", async () => {
  const output = document.querySelector("#result");

  try {
    const response = await fetch(document.querySelector("#url").value, {
      method: "GET",
      headers: {
        "X-Lab-Cors-Probe": "lab05"
      },
      credentials: "include"
    });

    output.textContent = `${response.status}: ${await response.text()}`;
  } catch (error) {
    output.textContent = `Browser blocked response: ${error.message}`;
  }
});

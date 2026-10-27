# HomeNet 🌐

> *A lightweight, custom-built web browser that takes the scenic route to the internet.*

**HomeNet** is an experimental desktop web browser built from scratch using C# and WPF. Instead of relying on pre-packaged browser engines, HomeNet is designed to handle DNS resolution, HTTP socket communication, and raw rendering from the ground up because doing things the easy way is boring.

---

## 🗺️ The Roadmap

Here is the master plan for bringing HomeNet to life, step by step:

* [x] **URL to IP Resolution:** Translating web addresses into raw IP addresses via DNS lookups.
* [ ] **HTTP Packet Fetching:** Writing the networking logic to establish connections and pull raw HTML packets from servers.
* [ ] **HTML Renderer:** Parsing markup text and turning it into actual visual UI elements inside the application.
* [ ] **CSS Renderer:** Adding styling support so websites don't look like an unformatted text document.
* [ ] **JavaScript Interpreter:** Integrating an external engine (or attempting to write a custom one and descending straight into madness).

---

## ⚙️ Tech Stack & Requirements

* **Framework:** WPF (.NET 10.0)
* **IDE:** Visual Studio 2026
* **Language:** C#

---

## 📄 License

Distributed under the [MIT License](LICENSE). Open-source for modding, breaking, and over-engineering.

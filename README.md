# AbxMockExchangeClient

A simple C# TCP client application that connects to a mock exchange server, retrieves market packets, handles missing packets, and saves the complete data to a JSON file.

## Features

- Connects to the server and requests all available packets.
- Detects and requests missing packets based on sequence numbers.
- Parses and stores packet data (Symbol, Buy/Sell, Quantity, Price, Sequence).
- Outputs all packets into a nicely formatted `packets.json` file.

## How to Run Locally

### Prerequisites

- .NET 6.0 SDK or later installed.
- Internet access to fetch any required packages (if necessary).

### Steps

1. Clone the repository:
    ```bash
    git clone https://github.com/YOUR_GITHUB_USERNAME/AbxMockExchangeClient.git
    ```

2. Navigate into the project directory:
    ```bash
    cd AbxMockExchangeClient
    ```

3. Restore dependencies:
    ```bash
    dotnet restore
    ```

4. Build the project:
    ```bash
    dotnet build
    ```

5. Run the project:
    ```bash
    dotnet run
    ```

After running, a `packets.json` file will be created in the project directory containing the collected and ordered packet data.

## Important Notes

- Make sure the mock server is running and accessible at `127.0.0.1:3000` before you run the client.
- The sequence number in resend requests is sent as a **single byte** (values should be between `0-255`).

---

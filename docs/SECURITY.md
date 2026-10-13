# Security

- The host listens on loopback only. Do not expose it through a reverse proxy or port-forwarding rule.
- Configure a high-entropy `PrintAgent:LocalToken`; browser CORS is not authentication.
- Only configured origins are allowed. Requests with an `Origin` outside the allowlist receive `403`.
- Keep printer addresses and Windows printer names in local configuration. They are never accepted from API clients.
- Raw ESC/POS is disabled by default because printer commands can open the cash drawer or cut paper.
- Receipt payloads and bytes are deliberately excluded from application logs. Put the database and configuration under an ACL restricted to the service account.

Rotate the token by replacing it in the protected configuration and restarting the service. Existing browser clients must then use the new token.

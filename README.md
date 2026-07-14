# TypedPond

TypedPond is a health-gated Windows laptop lock system that integrates step count data from an Android companion app with desktop security. The system tracks user activity through daily step goals stored in Firebase, then uses this health metric to automatically manage Windows laptop lock states based on activity thresholds.

This project contains a .NET 8 WPF application with a background service for Windows, a Kotlin Android companion app, and Firebase configuration for real-time data synchronization.

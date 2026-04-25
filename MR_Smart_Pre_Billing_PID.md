# Project Initiation Document (PID)

## Project Title
**Mixed Reality Based Smart Pre-Billing System for Grocery Stores**

## Project Type
Mini Project / Academic Innovation Project

## Prepared By
Apurv

## Project Overview
This project proposes a **Mixed Reality (MR) assisted grocery shopping and pre-billing system** that helps reduce congestion at billing counters. Using an MR device such as **Meta Quest 3**, smart glasses, or another camera-enabled wearable, customers can scan the barcode of products before placing them into their physical shopping cart. The scanned item details, such as product name, price, quantity, and offers, are displayed in the user’s view and simultaneously added to a **virtual shopping cart**.

By the time the customer reaches the checkout counter, the billing information is already prepared, reducing manual scanning effort and shortening waiting time. The system acts as an **assisted pre-billing solution** rather than a complete replacement for traditional billing.

## Problem Statement
In supermarkets and grocery stores, long queues at billing counters are a common problem, especially during peak hours. Traditional billing requires every item to be scanned at checkout, which increases customer waiting time and causes crowding near counters.

There is a need for a system that can:
- reduce checkout time,
- improve customer shopping experience,
- provide live cart and price visibility,
- and support faster billing using pre-collected item data.

## Proposed Solution
The proposed system uses **barcode scanning through an MR-enabled wearable device** to identify products during shopping. When the customer scans a product barcode, the system fetches the product details from a database and displays them in real time. The customer can confirm adding the item to the virtual cart through gesture, voice, or controller input.

The virtual cart maintains:
- item names,
- quantities,
- individual prices,
- total bill,
- and any applicable discounts.

At the billing counter, the virtual cart data is transferred to the billing system for quick verification and payment processing.

## Project Objectives
1. To design an MR-based product scanning interface for grocery shopping.
2. To read standard product barcodes using a normal RGB camera.
3. To display real-time product details in a virtual MR overlay.
4. To maintain a synchronized virtual cart during shopping.
5. To generate pre-billing information before the customer reaches checkout.
6. To reduce billing counter workload and customer waiting time.

## Scope of the Project

### In Scope
- Barcode detection and decoding using a camera
- Product database lookup
- MR display of product information
- Virtual cart creation and management
- Running total and bill generation
- Checkout handoff of cart data

### Out of Scope
- Full store-wide deployment
- Automatic payment processing
- Theft detection with complete accuracy
- Inventory management integration at enterprise scale
- Full replacement of cashier-based billing

## Key Features
- Barcode scanning using headset or smart-glasses camera
- Display of product name, price, quantity, and offers
- Add-to-cart confirmation through button, gesture, or voice
- Virtual cart with running total
- Remove/update item quantity
- Pre-billing summary at checkout
- Faster final verification process

## System Workflow
1. Customer wears MR device.
2. Customer scans product barcode before placing item in cart.
3. System decodes barcode and fetches product details from database.
4. Product information is shown in MR view.
5. Customer confirms “Add to Cart.”
6. Item is added to virtual cart and total bill updates.
7. At checkout, billing counter receives the virtual cart summary.
8. Cashier verifies and completes payment.

## Functional Requirements
- The system shall capture barcode images using an RGB camera.
- The system shall decode 1D/2D barcodes.
- The system shall fetch product data from a product database.
- The system shall display product details in MR view.
- The system shall allow users to add or remove products from the virtual cart.
- The system shall calculate total bill dynamically.
- The system shall generate a cart summary for billing.

## Non-Functional Requirements
- The system should respond in near real time.
- The user interface should be simple and readable.
- The system should work in indoor store lighting conditions.
- The scanning module should be reasonably accurate.
- The solution should be low-cost for prototype implementation.
- The system should be comfortable enough for short-duration use.

## Technologies and Tools

### Hardware
- Meta Quest 3 or similar MR headset for prototype
- Alternative concept device: smart glasses
- Computer/laptop for development
- Optional handheld controller or gesture input

### Software
- Unity for MR application development
- C# for application logic
- Barcode decoding library such as **ZXing**
- Database: Firebase / SQLite / MySQL
- MR SDK: Meta SDK / OpenXR
- Optional backend for billing sync

## Proposed Architecture

### Modules
1. **Camera Input Module**  
   Captures live video feed from MR device.

2. **Barcode Detection Module**  
   Detects and decodes barcode from image frames.

3. **Product Database Module**  
   Matches barcode with product details.

4. **MR Display Module**  
   Shows item details and interaction options in user view.

5. **Virtual Cart Module**  
   Stores selected products and updates running bill.

6. **Checkout Integration Module**  
   Sends cart summary to billing counter system.

## Feasibility Analysis

### Technical Feasibility
The project is technically feasible because barcode scanning can be done using standard RGB cameras and existing software libraries. MR display and interaction can be implemented using currently available development platforms like Unity and Meta SDK.

### Economic Feasibility
The prototype is economically feasible for academic purposes because it can be built using existing devices and open-source libraries. No special industrial scanner hardware is required for the initial demo.

### Operational Feasibility
The system is operationally feasible as an assisted shopping tool. However, for real-world deployment, customer comfort, scanning discipline, and verification mechanisms must be considered.

## Risks and Challenges
- Barcode may not always be clearly visible.
- Camera blur or poor lighting may affect scan accuracy.
- Users may forget to scan items.
- Scanned items may not match items physically placed in cart.
- Headsets may be bulky for prolonged grocery shopping.
- Real billing system integration may be limited in prototype stage.

## Risk Mitigation
- Use clear add-to-cart confirmation.
- Provide rescan and remove-item options.
- Use high-contrast UI for better readability.
- Keep prototype scope limited to assisted pre-billing.
- Use a small sample product database for reliable demo.
- Demonstrate concept on a controlled set of products.

## Expected Outcomes
- Successful barcode scanning using MR device camera
- Real-time display of product details
- Functional virtual cart with itemized total
- Demonstration of reduced checkout workload
- A working prototype proving the concept of MR-assisted pre-billing

## Deliverables
- MR prototype application
- Product database
- Virtual cart and billing summary module
- Final report
- System architecture diagram
- Demo presentation

## Success Criteria
The project will be considered successful if:
- the system can scan grocery item barcodes reliably,
- product details are displayed correctly,
- the virtual cart updates properly,
- and a billing summary is generated before checkout.

## Limitations
- Prototype may require manual confirmation for cart addition.
- It may not fully detect whether an item was physically placed in the cart.
- Real supermarket deployment would require stronger validation and anti-fraud mechanisms.
- Wearable comfort and usability may vary between devices.

## Future Enhancements
- Cart verification using weight sensors or cart-mounted camera
- Voice-based shopping assistant
- Budget tracking and purchase suggestions
- Personalized offers and discount alerts
- Indoor navigation to locate products
- Full smart-glasses implementation instead of bulky headset
- Integration with UPI/card payment and digital receipts

## Conclusion
The proposed **Mixed Reality Based Smart Pre-Billing System for Grocery Stores** is an innovative retail assistance solution aimed at reducing billing counter congestion and improving the customer experience. The project is suitable as a mini project because it combines practical computer vision, MR interaction, and billing workflow optimization in a manageable scope. While not intended to replace existing billing entirely, it serves as a strong prototype for assisted checkout systems of the future.
